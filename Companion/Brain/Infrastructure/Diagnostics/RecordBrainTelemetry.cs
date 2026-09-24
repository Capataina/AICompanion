#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;
using AICompanion.Companion.Brain.Infrastructure.Interactions.Firing;
using AICompanion.Companion.Brain.Infrastructure.Movement;
using AICompanion.Companion.Brain.Infrastructure.WeaponKnowledge.Learning;
using AICompanion.Companion.CharacterBody;

namespace AICompanion.Companion.Brain.Infrastructure.Diagnostics;

/// <summary>
/// The record of what the brain did, one tab-separated line per companion tick, written
/// to a file under the mod's own source folder so a session is diagnosed from the record
/// instead of from memory. It writes what the overlay shows and more: every action's raw
/// and final score, the reflex, danger and horizon, the request and the spot, the route
/// and its lookahead, the body's centre, velocity, clearance, wall contact and liquid, what
/// is in the hand, the light, and where the player is and what they are doing. A pinned
/// companion is a run of ticks holding a velocity without moving; a stuck one is a run of
/// ticks with the same tile, a route and no progress.
///
/// One file per world session, named by the clock, opened on world load and closed on
/// world unload or mod unload; the folder is ignored by git and by the mod packager. At
/// sixty lines a second a session runs to a few megabytes, which is the price of never
/// having to guess again.
/// </summary>
public sealed class BrainTelemetry : ModSystem
{
    private const int FlushEveryTicks = 60;

    private static StreamWriter? writer;
    private static FlushDiagnosticRecords? diagnosticWriter;
    private static int sinceFlush;
    private static bool headerWritten;
    private static string? censusPath;
    private static string? mapPath;
    private static string? eventsPath;
    private static readonly Stopwatch sessionClock = new();
    private static DateTime sessionStartedUtc;
    // 0.30.0 joins two branches that numbered their changes independently: the evidence branch's 0.25.0–0.29.0 are the
    // numbers SessionReport's version gates use, and the combat branch's own 0.25.0 and 0.26.0 columns are read by name.
    // 0.31.0 lands two lanes at once, and no played capture carries either alone. The light scalar becomes the two
    // field queries the torch decision actually reads (`dark_near`, `dark_ahead`, `torch_reason`, with
    // `light_samples`/`light_read_tick` renamed from the ambient pair), `light_region` names the dark region the
    // lighting activity nominated, `reach_n`/`returnable_n` become `reach_any`/`reach_two_way` after the reach sense
    // that now owns them with `reach_complete` beside them so an unfinished flood and a small one stop reading alike,
    // and the row ends with the travel rates while the route-episode and stop occurrences land beside them.
    // 0.32.0 appends what lighting's discovery search asked and what the reach sense answered
    // (`lighting_sites`, `lighting_sites_asked`), and declares `torch_reason` textual — which 0.31.0 wrote
    // and never declared, so every one of its rows read as a column that failed to parse as a number. The
    // declaration below is a hand-maintained parallel to the header builder and that omission is what it
    // costs; SessionReport now unions its own known-textual set with whatever a capture declares, so an
    // already-written capture is read correctly rather than only the next one.
    // 0.34.0 is the orb's schema: the walker's ground, press, descend, divergence, breath, edge and
    // observed/predicted body columns are gone, and the body row carries its wall contact and
    // normal, liquid, clearance, desired velocity and the route's lookahead instead. Columns are
    // addressed by name and the reader skips a check whose columns are absent, so an older capture
    // reads as reduced coverage rather than as a misread column.
    // 0.35.0 changes one column's meaning and appends the rest at the end of the row. `plan_ms` is this tick's own
    // planning cost, zero on a tick that planned nothing; before, it repeated the last plan's cost on every row until the
    // next one, so a sum over a stretch counted one plan once per row. Appended: `<activity>_time`, the time-per-job factor
    // each activity's final carried; `<activity>_funnel` for every activity that keeps a candidate funnel, naming the stage
    // that refused the candidate that got furthest; the player's own smart-cursor torch tile, its light, its placement
    // reading and the stage lighting refuses it at (`torch_reference`, `torch_reference_light`, `torch_reference_dark`,
    // `torch_reference_stage`); and this tick's garbage collections per generation (`gc0`, `gc1`, `gc2`). The decision
    // occurrence's factor list gains `time`, `player-fit` and `order`, with `raw` and `final` beside them, so the factors
    // recorded multiply to the final recorded; a new `candidate-funnel` occurrence carries each funnel's counts and entries.
    // A 0.34.0 capture reads as it did: every check addresses columns by name and skips one whose columns are absent.
    // 0.36.0 removes every column whose producer went when the owner ruled on 15 September 2026 that every liquid is air to
    // the orb: `hurting` and `liquid_ticks` from the body line; the escape's `escape_active`, `escape_stage` and
    // `escape_target` with the state search's `state_search_pending` and `state_search_retained`; the five `safety_*`
    // columns of a response that no longer exists; and the evade layer's `evade_wet_tick` and `evade_refused_liquid`. The
    // navigation evidence loses the matching keys, `self_danger` loses its trailing `L`, and the capabilities header reads
    // `liquids=air`. `wet` and `liquid` stay: which liquid the body is in is still observed, and nothing decides from it.
    // A 0.35.0 capture reads as it did, for the same reason as every bump before it.
    // 0.37.0 changes no column: the sidecar gains an `experience-credit` occurrence for every credit the experience ledger
    // takes, so a capture shows the bar moving and which kill, fight or piece of work moved it. The liquids and experience
    // lanes each moved the schema to 0.36.0 on their own branches; merging both made this bump, so no two captures with
    // different sidecars share a number.
    // 0.38.0 removes `transient_lights`. The light sense reads the world's own light from the engine's scan, taken before the
    // blur merges anything a body carries, so it discounts no carried light and a count of them describes nothing the brain
    // reads. `torch_reference_dark` no longer writes `carried`: a tile a carried light stands over reads dark or lit by the
    // world's own light like any other tile, and a 0.35.0 to 0.37.0 capture that wrote `carried` is read as it was written.
    // 0.39.0 relabels the merged combat stance. The `action` column, the decision factors, the method assessments and the
    // per-activity `combat_time`/`combat_funnel` columns write `combat` where they wrote `hunt` and `guard`; `fire` gains
    // `not-fighting` for a tick combat is not running; the configuration preamble writes `combat=` where it wrote
    // `hunting=`. The `guard_*`, `hunt_*` and `pursuit_*` columns keep their names and meanings, now written from the one
    // activity. A 0.38.0 capture reads as it was written: every check addresses columns by name, accepts the old activity
    // labels, and skips one whose columns are absent.
    // 0.41.0 begins retained-course evidence. Older captures have no typed course payloads and
    // SessionReport must call that historical-unavailable rather than infer an empty course.
    // 0.42.0 — the decision occurrence carries the course's own alternatives (`course-decision`,
    // `course:<domain>`, `course-admitted:<domain>`, `course-refused:<reason>`). It moves the version
    // rather than riding as a pure append because the same occurrence stopped carrying `family:<name>`
    // entries when the tick switched to the course at 0bb2c8a: `LastNominations` is empty, so the loop
    // that writes them produces nothing. A reader of a 0.41.0-or-earlier capture finds family
    // nominations and no course entries; a reader of this one finds the reverse, and the absence is the
    // change the append convention cannot carry.
    // 0.43.0 — `choice_id` and `choice_tick` are the course's decision identity and its source tick,
    // where they were the family chooser's `EvaluationId`. A column whose *meaning* moves is the one
    // change the append convention cannot carry, so it takes a version even though no column moved
    // position. A reader of 0.42.0 or earlier is reading the chooser's comparison; from here it is the
    // course's, and between the tick switch and this bump it was the chooser's frozen at zero.
    //
    // The identity advances on a changed settled decision rather than per tick, because a course is
    // carried until its next use stops validating and a held choice outliving its rescore is the design
    // rather than churn.
    // 0.44.0 moves three column families off the retired family chooser and onto the course, with no name
    // and no index changing: `<activity>_raw` / `_fin` now carry what the best order that domain leads was
    // worth, and `<activity>_offer` carries the course's own three-valued census admission. The schema
    // moves because the *meaning* moved, which is the case this project's own reader guide says needs a
    // witness rather than trust in a capture being new. What it fixes is not cosmetic: those columns had
    // read the literal `0.00` and `not-compared` in every played session since `0bb2c8a` put the course on
    // the tick and left `Chooser.LastScores` unfilled, and the three checks that grade a fight —
    // `CheckTheFight`, `CheckThePlayersReference`, `CheckOffersAttemptsAndGrants` — take exactly those
    // columns as their evidence, so all three were grading constants. `<activity>_time` stays at 1.000 and
    // is documented at its site as constant by design, because the course has no per-activity time factor
    // to report and inventing one would be the very substitution this bump exists to declare.
    // 0.46.0 removes every column and every board entry this folder wrote from the family chooser, which
    // `AIC-419` deleted on 22 September 2026. A removal is the one change the append convention cannot
    // carry, which is the whole reason the version moves; nothing else in the row changed, and every
    // reader here and in SessionReport addresses columns by name, so a 0.46.0 capture reads as absent
    // columns rather than as shifted ones. What went, and why each was safe to take:
    //
    //   <family>_prepared / _deferred / _prepare_ms   the per-family preparation scheduler's counts and
    //                                                 milliseconds, three columns per family. Filled only
    //                                                 by `Chooser.Queries`, so -1/-1/-1 in every row of
    //                                                 every capture since `0bb2c8a`.
    //   <activity>_time                               the chooser's per-activity time discount. Pinned at
    //                                                 1.000 by decision in 0.44.0, because the course has
    //                                                 no per-activity time factor and inventing one would
    //                                                 put a different quantity under a known name. A
    //                                                 column that is a constant by design is a column with
    //                                                 nothing to say, and it goes with the procedure it
    //                                                 described rather than staying as a placeholder.
    //   <activity>_funnel, the `candidate-funnel`     every candidate an activity's *preparation* refused,
    //   occurrence                                    named with the stage that refused it. Its subject is
    //                                                 a preparation-time shortlist the course does not
    //                                                 keep; the course's own equivalent is
    //                                                 `course-admitted:<domain>` beside
    //                                                 `course-refused:<reason>`, which is per domain and
    //                                                 already in the decision occurrence since 0.42.0.
    //   the decision board's score list, its          `Chooser.LastScores` and `LastNominations`, both
    //   `factors:` breakdown and `family:` entries    empty since the tick switch. 0.42.0 already replaced
    //                                                 them with the course's three questions.
    //   the board's `queries:<Family>=` entries       the same scheduler as the three columns above.
    //
    // What is deliberately *not* removed with them: `reunion_apart_ticks`, `reunion_departure` and
    // `reunion_delay_cost_per_tick`, and the board's `regroup=` and `return-ticks=`. Those were never the
    // chooser's — `ObserveCompanionship` runs them on every brain tick and `KeepCompany` reads the regroup
    // urgency — and the day they *looked* like the chooser's is the reason this folder's first trap exists.
    //
    // 0.47.0 is one version for the whole of wave 1 on 23 September 2026, where every work activity was
    // moved onto the course's bound step and pots were folded into collection. No TSV column moves.
    //
    //   removed   the `pot-target` domain. Domains are dynamic in the record, so what vanishes is every
    //             `course-admitted:pot-target=`, `course:pot-target=` and `<activity>_offer` reading of
    //             it; a pot is a `collect-target` opportunity whose need is a Container. A removal is the
    //             one change the append convention cannot carry, so the version moves.
    //   added     `binding-id`, `binding-origin` (activity, incidental, none, or not-an-effect /
    //             not-claimed for an occurrence that is not a decision's effect) and `binding-verdict`
    //             (bound, effect-without-binding, effect-off-binding, unaudited) at the end of every
    //             `tool-effect`, `world-interaction` and `pickup` detail, so a reader joins an effect to
    //             the step that chose it; the `contract-violation` occurrence's two new kinds of the same
    //             names; `container-usable:N` inside each `course-admitted:` entry, the usable candidates
    //             of that domain carrying a Container need, which is how a pot is still countable once
    //             it shares a domain with drops; and `effects-audited` on the `# closing=` line.
    //
    // A reader gates the pot witness on this version: below it a pot is `pot-target`'s usable count, from
    // it the Container count; a capture on either side reads as it was written.
    //
    // 0.48.0 says where inside a tick the time and the memory went, and which ticks stood out. It moves no column
    // and removes nothing; it is a version rather than a silent append because the session reader's "where the time
    // goes" block and its section measure decline a capture below it by name, and a gate needs a number to read.
    //
    //   added     at the end of the row: `tick_alloc_bytes` (the update thread's allocation since the previous row,
    //             `-` on a session's first), `brain_alloc_bytes` (the brain tick's own, `-` on a tick the brain did
    //             not run), `sections` (the section profiler's `SectionsPerRow` largest self times this tick as
    //             `path=self-ms/calls` joined by `|`, textual and declared), `sections_other_ms` (every other
    //             section's self time, so the two sum to the profiled total), `cost_fence_ms` (the fence this
    //             tick's `brain_ms` was judged against, `-` while the window fills) and `alloc_sections` (the
    //             `AllocatorsPerRow` sections that allocated the most outside their children, `path=bytes`
    //             joined by `|`, textual and declared). The sidecar gains the
    //             `cost-spike` occurrence — the worst brain tick of each one-second window whose cost crossed the
    //             fence, with its whole section tree, collections, allocation and scene counts — and the `# closing=`
    //             line gains `cost-spikes`, `cost-spike-dumps`, and the profiler's own failure counts
    //             `profiler-overflowed` and `profiler-unbalanced`. `ProfileBrainSections.cs` and
    //             `DetectCostSpikes.cs` own the two mechanisms. The `record` subtree of a row's `sections` is the
    //             previous row's recorder, the same phase `record_ms` has.
    private const string Schema = "0.48.0";

    /// <summary>
    /// One activity's factors from one comparison, as <c>name:value</c> pairs joined by commas: every multiplier its final
    /// <summary>
    /// What the course thought this activity's work was worth, under the activity's own column names.
    ///
    /// **These two columns read the course since schema 0.44.0, and read the retired family chooser
    /// before it, which is why the schema moved for a pair of columns whose names did not.** From
    /// `0bb2c8a` — the commit that put the course on the tick — until that schema, `Chooser.LastScores`
    /// was never filled in a played session, so every `<activity>_raw` and `<activity>_fin` in every
    /// capture was the literal `0.00`. That is not a cosmetic gap: `CheckTheFight` names `combat_raw` and
    /// `combat_fin` in its `Needs` and `CheckThePlayersReference` reads four of these columns, so the two
    /// checks that grade a fight were grading constants, and a reader comparing a course capture against
    /// a pre-`0bb2c8a` one would have read the brain as having stopped valuing anything at all.
    ///
    /// The quantity genuinely changed with the brain and the schema is the witness for that: the chooser
    /// scored each activity independently every tick, where the course scores whole orders and reports the
    /// best order each domain leads. So `raw` is that leader's nominal total and `fin` is what survives
    /// its harm and companionship terms — the same two questions the old pair answered, computed by a
    /// different procedure. An activity performing more than one domain reports its best, which is what
    /// the offer column beside it already did across collection's two methods.
    ///
    /// Zero for an activity the course minted no opportunity for is honest rather than missing: keeping
    /// company declares no domain at all, because an empty course *is* companionship.
    /// </summary>
    private static (float Raw, float Final) CourseWorthOf(Brain brain, Activities.CompanionAction action)
    {
        var worth = ReadCourseWorthPerActivity.Of(brain, action);
        return (worth.Raw, worth.Final);
    }

    // The garbage collector's per-generation counts at the last row, so each row carries its own tick's collections; -1 until
    // a session's first row has read them.
    private static int lastGc0 = -1, lastGc1 = -1, lastGc2 = -1;
    // The cost of the previous row's Record call: a row cannot contain the time spent writing itself, so each row carries
    // the one before it and the first row of a session carries none.
    private static double lastRecordMs = double.NaN;

    // Schema 0.48.0: where the time went inside the tick, and the ticks that stood out.
    private static readonly int RecordSection = BrainSections.Register("record");
    private static readonly int RecordOccurrencesSection = BrainSections.Register("occurrences");
    private static readonly int RecordRowSection = BrainSections.Register("row");
    private static readonly int RecordEnqueueSection = BrainSections.Register("enqueue");
    /// <summary>
    /// How many sections the <c>sections</c> column names per row, most self time first. Eight, because on the
    /// section-profile scenes of 24 September 2026 a tick entered about thirty sections and the eight largest held
    /// nearly all of its self time, and a row already carries a few hundred columns: the column is read for which
    /// sections dominated a tick, and the ninth-largest has not dominated anything. What the eight leave out is
    /// never lost — <c>sections_other_ms</c> carries its total, and a spike tick's occurrence carries the whole tree.
    /// </summary>
    internal const int SectionsPerRow = 8;
    private static readonly int[] topSections = new int[SectionsPerRow];
    /// <summary>How many sections the <c>alloc_sections</c> column names per row, most bytes allocated outside their
    /// children first. Four, because allocation concentrates harder than time: on the replayed 22 September capture of
    /// 24 September 2026 a brain tick allocated about 2 MB at the median and a handful of sections held nearly all of it.</summary>
    internal const int AllocatorsPerRow = 4;
    private static readonly int[] topAllocators = new int[AllocatorsPerRow];
    // The thread's allocation counter at the previous row, so each row carries the bytes the game's update thread
    // allocated between two rows — everything on that thread, this mod and the engine alike; -1 before a session's
    // first row.
    private static long lastThreadAllocated = -1;
    // The brain-cost fence, over this session's own brain ticks. `DetectCostSpikes` explains the rule.
    private static readonly DetectCostSpikes costFence = new();
    /// <summary>Spike ticks this session, every one counted whatever the dump's rate limit let through.</summary>
    internal static long CostSpikes { get; private set; }
    /// <summary>`cost-spike` occurrences written this session: one per window, for the worst spike in it.</summary>
    internal static long CostSpikeDumps { get; private set; }
    /// <summary>
    /// Ticks a spike window stays open. The first spike opens it, the worst spike inside it is the one written, and
    /// it is written when the window closes, so a burst of consecutive spikes is one record naming how many there
    /// were rather than a record a tick. One second at the engine's sixty a second: a rate on writing, not a
    /// threshold on cost.
    /// </summary>
    internal const int CostSpikeWindowTicks = 60;
    private static ulong spikeWindowOpenedAt;
    private static int spikesInWindow;
    private static string? pendingSpike;
    private static double pendingSpikeCost;
    /// <summary>What the last Record call cost in milliseconds, NaN before the first; MeasureBrainCost samples it as its own phase.</summary>
    public static double LastRecordMilliseconds => lastRecordMs;
    // Rows this session has written, which the end marker states so a reader can tell a file that lost rows from one
    // that never had them.
    private static int rowsWritten;
    /// <summary>The brain cost of the row before this one. The frame ledger's remainder subtracts it
    /// rather than this row's, because an interval closed at the top of update N spans update N−1 —
    /// the same reason `record_ms` is already the previous row's.</summary>
    private static double lastBrainMs;
    // One `frame-overrun` occurrence per window rather than per overrunning frame. On the 22 September
    // capture 84% of frames exceeded the budget, so an occurrence each would be two thousand records
    // saying one thing; the window carries how many there were and the worst one's whole split, which
    // is what a reader of a hitch actually opens. The share across the session is read off the
    // `frame_ms` column instead, where every row has one.
    private const int OverrunWindowTicks = 30;
    private static ulong overrunWindowOpenedAt;
    private static int overrunsInWindow;
    private static double worstFrameInWindow;
    private static string worstFrameSplit = "";
    private static RecordedConfiguration recordedConfiguration;
    /// <summary>The revision and tree state AICompanion.csproj's StampSourceRevision target stamped into this assembly,
    /// read once; "unknown" when that build could not ask git.</summary>
    private static readonly string SourceProvenance = ReadSourceProvenance();

    private static string ReadSourceProvenance()
    {
        string Stamp(string key) => typeof(BrainTelemetry).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyMetadataAttribute), false)
            .OfType<System.Reflection.AssemblyMetadataAttribute>().FirstOrDefault(attribute => attribute.Key == key)?.Value is { Length: > 0 } value ? value : "unknown";
        return $"{Stamp("SourceRevision")};tree={Stamp("SourceTree")}";
    }

    /// <summary>
    /// The per-character preferences and diagnostics switches a capture runs under, as one comparable value: the
    /// preamble states the value at the start and an occurrence records each change, compared every row without
    /// allocating, because a preference changed from the profile card mid-session changes what the companion does.
    /// </summary>
    private readonly record struct RecordedConfiguration(Activities.WorkPolicy Mining, Activities.WorkPolicy Chopping, bool Combat, bool PotBreaking,
        bool TorchPlacement, PlayerIntegration.CompanionDistanceMode DistanceMode, bool Inspector, bool RecordTelemetry)
    {
        public static RecordedConfiguration Current()
        {
            var preferences = PlayerIntegration.CompanionPreferences.Current;
            var switches = DiagnosticsConfiguration.CompanionDiagnosticsConfig.Current;
            return new(preferences.Mining, preferences.Chopping, preferences.Combat, preferences.PotBreaking, preferences.TorchPlacement,
                preferences.DistanceMode, switches.EnableBrainInspector, switches.RecordTelemetry);
        }

        public string Describe()
            => $"character;mining={Mining};chopping={Chopping};combat={Flag(Combat)};pot_breaking={Flag(PotBreaking)};torch_placement={Flag(TorchPlacement)};distance_mode={DistanceMode};inspector={Flag(Inspector)};record_telemetry={Flag(RecordTelemetry)}";

        private static string Flag(bool value) => value ? "true" : "false";
    }
    private static string? pendingPlayerHit;
    private static string? pendingCompanionHit;
    private static string? lastDecision;
    private static bool firstUpdateRecorded;

    /// <summary>The folder the files land in: the mod's source folder, which is where the repository is.</summary>
    public static string Folder => Path.Combine(Main.SavePath, "ModSources", "AICompanion", "Telemetry");
    internal static double ElapsedMilliseconds => sessionClock.Elapsed.TotalMilliseconds;

    /// <summary>Stands the decision audit's reader up before any world opens, and whether or not
    /// recording is switched on, because the audit's source is wiring rather than session state.</summary>
    public override void Load() => ReadLiveCourseForAudit.Install();

    public override void OnWorldLoad()
    {
        Close("superseded-by-world-load");
        if (diagnosticWriter is { Completed: false })
        {
            Mod.Logger.Warn("BrainTelemetry: previous recording is still closing; refusing a replacement capture.");
            return;
        }
        if (!DiagnosticsConfiguration.CompanionDiagnosticsConfig.Current.RecordTelemetry) return;
        try
        {
            Directory.CreateDirectory(Folder);
            string stamp = $"{DateTime.UtcNow:yyyy-MM-dd_HH-mm-ss-fff}";
            string path = ReserveSessionPath(stamp, out string sessionStem);
            plansPath = Path.Combine(Folder, $"{sessionStem}-plans.txt");
            censusPath = Path.Combine(Folder, $"{sessionStem}-census.txt");
            mapPath = Path.Combine(Folder, $"{sessionStem}-map.txt");
            eventsPath = Path.Combine(Folder, $"{sessionStem}-events.jsonl");
            lastDumpTick = -DumpEveryTicks;
            headerWritten = false;
            rowsWritten = 0;
            lastRecordMs = double.NaN;
            sessionStartedUtc = DateTime.UtcNow;
            sessionClock.Restart();
            writer?.Dispose(); writer = null;
            diagnosticWriter = FlushDiagnosticRecords.Start(path, eventsPath);
            GodsEyeEvents.Open(eventsPath);
            WriteMetadata();
            GodsEyeEvents.RecordLifecycle("world-entry", "observed=ModSystem.OnWorldLoad;tag-load=not-yet-observed;outer-load=unobservable");
            pendingPlayerHit = null;
            pendingCompanionHit = null;
            lastDecision = null;
            firstUpdateRecorded = false;
            ScenarioCapture.Reset();
            AuditDecisionContracts.Reset();
            FrameCost.Reset();
            // The identities are per session, so a previous world's drop cannot suppress this one's
            // first sighting of the slot it happens to reuse.
            sightedDrops.Clear();
            lastBrainMs = 0;
            // The fence is this session's own: a previous world's ticks say nothing about this one's.
            costFence.Reset();
            CostSpikes = CostSpikeDumps = 0;
            spikesInWindow = 0;
            pendingSpike = null;
            lastThreadAllocated = -1;
            overrunWindowOpenedAt = Main.GameUpdateCount;
            overrunsInWindow = 0;
            worstFrameInWindow = 0;
            worstFrameSplit = "";
            TravelEpisodes.Reset();
            BehaviourCensus.Reset();
            SessionMap.Reset();
            Mod.Logger.Info($"BrainTelemetry: writing {path}");
        }
        catch (Exception e)
        {
            // Reservation can succeed before a sidecar or later initialisation fails. Release
            // that stream here rather than abandoning the handle until garbage collection.
            GodsEyeEvents.RecordLifecycle("recorder-initialization-failed", "capture=incomplete;outer-load=unobservable");
            GodsEyeEvents.Close();
            try { writer?.Dispose(); }
            catch (Exception closeError) { Mod.Logger.Warn($"BrainTelemetry: cleanup after open failure: {closeError.Message}"); }
            finally
            {
                QueueDiagnosticRecords.Fail("recorder-initialization-failed");
                diagnosticWriter?.Stop(TimeSpan.FromMilliseconds(100), "recorder-initialization-failed", rowsWritten);
                if (diagnosticWriter?.Completed == true) diagnosticWriter = null;
                writer = null;
                plansPath = censusPath = mapPath = eventsPath = null;
                sessionClock.Reset();
            }
            Mod.Logger.Error($"BrainTelemetry: could not open a file under {Folder}: {e.Message}");
        }
    }

    public override void OnWorldUnload()
    {
        Close("world-unload");
        global::AICompanion.Companion.Brain.Infrastructure.Observation.PredictObservedMotion.Clear();
    }

    public static void StopRecording() => Close("recording-disabled");

    public override void LoadWorldData(TagCompound tag)
    {
        GodsEyeEvents.RecordLifecycle("tag-load-entered", "observed=BrainTelemetry.LoadWorldData;outer-load=unobservable");
        // Returning from this callback proves this mod's callback finished. It cannot prove that
        // another ModSystem callback or Terraria's outer load completed after it.
        GodsEyeEvents.RecordLifecycle("tag-load-returned", "observed=BrainTelemetry.LoadWorldData-returned;outer-load=unobservable");
    }

    public override void SaveWorldData(TagCompound tag)
    {
        GodsEyeEvents.RecordLifecycle("save-entered", "observed=BrainTelemetry.SaveWorldData;world-persisted=unobservable");
        GodsEyeEvents.RecordLifecycle("save-returned", "observed=BrainTelemetry.SaveWorldData-returned;world-persisted=unobservable");
    }

    public override void PostUpdateEverything()
    {
        if (!DiagnosticsConfiguration.CompanionDiagnosticsConfig.Current.RecordTelemetry)
        {
            if (diagnosticWriter != null) Close("recording-disabled");
            return;
        }
        if (diagnosticWriter == null || firstUpdateRecorded)
            return;
        firstUpdateRecorded = true;
        GodsEyeEvents.RecordLifecycle("first-update", "observed=ModSystem.PostUpdateEverything;outer-load=unobservable");
    }

    /// <summary>
    /// Receives Terraria's final hurt calculation from the local player's ModPlayer hook. A life
    /// drop proves neither the hit source nor knockback; this bounded event supplies both.
    /// </summary>
    public static void RecordPlayerHurt(Player.HurtInfo info)
    {
        string source = "unidentified";
        if (info.DamageSource.TryGetCausingEntity(out Entity entity))
            source = entity switch
            {
                NPC npc => $"npc:{npc.TypeName}",
                Projectile projectile => $"projectile:{projectile.type}",
                _ => entity.GetType().Name,
            };
        pendingPlayerHit = $"damage={info.Damage};source={source};direction={info.HitDirection};knockback={info.Knockback.ToString("0.00", CultureInfo.InvariantCulture)}";
    }

    /// <summary>
    /// Receives the NPC damage callback. NPC hurt information has no attacker source, so this
    /// preserves only the exact final damage, direction and knockback rather than inventing one.
    /// </summary>
    public static void RecordCompanionHit(NPC.HitInfo info)
        => pendingCompanionHit = $"damage={info.Damage};direction={info.HitDirection};knockback={info.Knockback.ToString("0.00", CultureInfo.InvariantCulture)};source=unrecorded";

    public override void Unload()
    {
        Close("mod-unload");
        global::AICompanion.Companion.Brain.Infrastructure.Observation.PredictObservedMotion.Clear();
        Mod.Logger.Info("BrainTelemetry.Unload: done");
    }

    /// <summary>A normal closure, named by <paramref name="reason"/>. Only this writes the end marker: a failed row write
    /// disposes the stream without coming here, so a capture that lacks the marker was interrupted.</summary>
    private static void Close(string reason)
    {
        if (diagnosticWriter == null) return;
        // The census and the map are written here rather than per tick because both are one
        // artefact about the whole session; there is nothing to say until it is over. They land
        // beside the .tsv under the same stamp, so the session reader finds them without being
        // told where to look.
        WriteWhole(censusPath, BehaviourCensus.Report, "census");
        WriteWhole(mapPath, SessionMap.Report, "map");
        // The open journey and the open stop belong to this session, so they are written before the stream closes; the
        // census closes its own open episode inside Report for the same reason.
        TravelEpisodes.Close(CompanionNPC.Instance);
        // The decision audit coalesces its violations, so the totals in the capture are the totals as
        // at the last record it wrote; this enqueues one closing record per kind that has moved since,
        // while the event stream is still taking them.
        AuditDecisionContracts.Flush();
        // A spike window still open when the session ends is written rather than lost, for the same reason.
        FlushCostSpike(CompanionNPC.Instance?.NPC);
        GodsEyeEvents.Close();
        try
        {
            // A session that recorded no row at all still says what it ran under. The line is written
            // from the first row because that is the first moment the character's saved preferences
            // exist; a capture with no rows never reached it, and before this it closed carrying no
            // `# config=` line of any kind — which reads as a capture from before 0.28.0 rather than as
            // a session that opened and recorded nothing. The value is read now rather than at world
            // entry for the same reason the row's is, so it is the preferences as they stand.
            if (!headerWritten)
                QueueDiagnosticRecords.TryEnqueueTsv($"# config={RecordedConfiguration.Current().Describe()}");
            // `decisions-audited` and `audit-observations-read` are the capture's own witnesses to the
            // audit's two wirings, and they are here rather than in the header because the header is
            // written on the first row, before any decision has been made. A reader grades them against
            // the count of `course-decision` occurrences: decisions with nothing audited is the hook
            // gone from `RecordCourseTrace.Record`, and decisions audited with nothing read is
            // `ReadLiveCourseForAudit.Install` never having run. Both are silent in play otherwise.
            // `effects-audited` (0.47.0) is the effect contract's denominator: a session whose hand did
            // nothing has two zero violation counts that mean nothing, and this is what says so.
            QueueDiagnosticRecords.TryEnqueueTsv($"# closing={reason};rows={rowsWritten};events-offered={GodsEyeEvents.Written};events-dropped={GodsEyeEvents.Dropped};events-coalesced={GodsEyeEvents.Coalesced};terrain-evictions={RecordTerrainChunks.Evictions};decisions-audited={AuditDecisionContracts.Audited};audit-observations-read={AuditDecisionContracts.ObservationsRead};effects-audited={AuditDecisionContracts.EffectsAudited};cost-spikes={CostSpikes};cost-spike-dumps={CostSpikeDumps};profiler-overflowed={BrainSections.Overflowed};profiler-unbalanced={BrainSections.Unbalanced}");
            diagnosticWriter.Stop(TimeSpan.FromMilliseconds(100), reason, rowsWritten);
        }
        catch (Exception e)
        {
            ModContent.GetInstance<AICompanion>().Logger.Warn($"BrainTelemetry: close failed: {e.Message}");
        }
        finally
        {
            sessionClock.Reset();
            writer = null;
            if (diagnosticWriter?.Completed == true) diagnosticWriter = null;
            plansPath = null;
            censusPath = null;
            mapPath = null;
            eventsPath = null;
        }
    }

    private static string ReserveSessionPath(string timestamp, out string sessionStem)
    {
        Directory.CreateDirectory(Folder);
        for (int attempt = 0; ; attempt++)
        {
            sessionStem = attempt == 0 ? timestamp : $"{timestamp}-{attempt}";
            string candidate = Path.Combine(Folder, $"{sessionStem}.tsv");
            try
            {
                writer = new StreamWriter(new FileStream(candidate, FileMode.CreateNew, FileAccess.Write, FileShare.Read), Encoding.UTF8);
                return candidate;
            }
            catch (IOException) when (File.Exists(candidate))
            {
                // A retry in the same clock instant is a second capture, never permission to
                // replace the evidence from the first one.
            }
        }
    }

    /// <summary>
    /// Schema 0.48.0's six columns: the update thread's allocation since the previous row, the brain tick's own
    /// allocation, the section profiler's largest self times this tick, the rest of them, the fence this tick's
    /// brain cost was judged against, and the sections that allocated the most. A dash wherever the thing did not happen this tick — no previous row, no brain
    /// tick, no snapshot for this tick, a window still filling — because a zero there would be a measurement.
    /// </summary>
    private static void AppendCostColumns(StringBuilder sb, Brain brain, bool brainExecuted)
    {
        long threadNow = GC.GetAllocatedBytesForCurrentThread();
        sb.Append('\t');
        if (lastThreadAllocated < 0) sb.Append('-');
        else sb.Append(threadNow - lastThreadAllocated);
        lastThreadAllocated = threadNow;

        bool fresh = BrainSections.LastTick == Main.GameUpdateCount;
        sb.Append('\t');
        if (fresh && brainExecuted) sb.Append(BrainSections.LastBrainAllocatedBytes);
        else sb.Append('-');

        sb.Append('\t');
        if (!fresh) sb.Append("-\t-");
        else
        {
            int listed = BrainSections.TopBySelf(topSections, SectionsPerRow);
            double listedMs = 0, allMs = 0;
            for (int i = 0; i < listed; i++)
            {
                int node = topSections[i];
                if (i > 0) sb.Append('|');
                sb.Append(BrainSections.Path(node)).Append('=')
                    .Append(CultureInfo.InvariantCulture, $"{BrainSections.SelfMilliseconds(node):0.000}/{BrainSections.Calls(node)}");
                listedMs += BrainSections.SelfMilliseconds(node);
            }
            if (listed == 0) sb.Append('-');
            for (int node = 1; node < BrainSections.LastNodeCount; node++)
                if (BrainSections.Calls(node) > 0) allMs += BrainSections.SelfMilliseconds(node);
            sb.Append('\t').Append(CultureInfo.InvariantCulture, $"{Math.Max(0, allMs - listedMs):0.000}");
        }

        sb.Append('\t');
        if (brainExecuted && double.IsFinite(costFence.Last.Fence))
            sb.Append(CultureInfo.InvariantCulture, $"{costFence.Last.Fence:0.000}");
        else sb.Append('-');

        // The sections that allocated the most outside their own children, as `path=bytes`, because a collection
        // is paid for by whoever allocated and the section tree is the only thing that can say who that was.
        sb.Append('\t');
        int allocators = fresh ? BrainSections.TopBySelfAllocation(topAllocators, AllocatorsPerRow) : 0;
        int written = 0;
        for (int i = 0; i < allocators; i++)
        {
            int node = topAllocators[i];
            if (BrainSections.SelfAllocatedBytes(node) <= 0) continue;
            if (written++ > 0) sb.Append('|');
            sb.Append(BrainSections.Path(node)).Append('=').Append(BrainSections.SelfAllocatedBytes(node));
        }
        if (written == 0) sb.Append('-');
    }

    /// <summary>
    /// Judges this tick's brain cost against the session's own recent ticks and keeps the worst spike of the open
    /// window, writing it when the window closes. Only a tick the brain ran on is judged, because a downed tick's zero
    /// is not a cheap tick. The fence is allocation-free; describing a spike allocates, and only on a tick that is
    /// worse than every other spike in its window.
    /// </summary>
    private static void ObserveCostSpike(Brain brain, NPC npc, bool brainExecuted)
    {
        if (brainExecuted)
        {
            DetectCostSpikes.Verdict verdict = costFence.Observe(brain.TotalMs);
            if (verdict.Spike)
            {
                CostSpikes++;
                if (spikesInWindow == 0) spikeWindowOpenedAt = Main.GameUpdateCount;
                spikesInWindow++;
                if (pendingSpike == null || verdict.Cost > pendingSpikeCost)
                {
                    pendingSpike = DescribeSpike(brain, verdict);
                    pendingSpikeCost = verdict.Cost;
                }
            }
        }
        if (spikesInWindow > 0 && Main.GameUpdateCount - spikeWindowOpenedAt >= CostSpikeWindowTicks) FlushCostSpike(npc);
    }

    private static void FlushCostSpike(NPC? npc)
    {
        if (spikesInWindow > 0 && pendingSpike != null)
        {
            GodsEyeEvents.RecordCostSpike(npc, pendingSpikeCost, spikesInWindow, CostSpikeWindowTicks, pendingSpike);
            CostSpikeDumps++;
        }
        spikesInWindow = 0;
        pendingSpike = null;
        pendingSpikeCost = 0;
    }

    /// <summary>
    /// The spike's whole account: the tick and its cost, the fence and how it was computed, what the tick allocated
    /// and collected, what the brain was holding, and the whole section tree. Scene counts are what the brain already
    /// holds, read rather than recomputed: hostiles and projectiles the threat senses hold, drops the loot sense holds,
    /// the frozen observation's facts and its light sites, the census's candidates, the orders the last search
    /// priced, whether a decision is in flight and how many model queries it is waiting on, and the reach flood's size.
    /// </summary>
    private static string DescribeSpike(Brain brain, DetectCostSpikes.Verdict verdict)
    {
        var senses = brain.Senses;
        var facts = brain.Course.Facts?.Facts;
        int lightSites = 0;
        if (facts != null)
            foreach (var fact in facts)
                if (fact.Key.Kind == "light-target") lightSites++;
        long threadAllocated = lastThreadAllocated < 0 ? -1 : GC.GetAllocatedBytesForCurrentThread() - lastThreadAllocated;
        var detail = new StringBuilder(1024);
        detail.Append(CultureInfo.InvariantCulture,
            $"tick={Main.GameUpdateCount};brain-ms={verdict.Cost:0.000};fence-ms={verdict.Fence:0.000};q1-ms={verdict.LowerQuartile:0.000};median-ms={verdict.Median:0.000};q3-ms={verdict.UpperQuartile:0.000}");
        detail.Append(";rule=").Append(verdict.Rule(costFence.Window));
        detail.Append(CultureInfo.InvariantCulture,
            $";phases=senses:{brain.SensesMs:0.000},reflex:{brain.ReflexMs:0.000},decide:{brain.DecideMs:0.000},position:{brain.PositionMs:0.000},navigate:{brain.NavigateMs:0.000},finalise:{brain.FinaliseMs:0.000}");
        detail.Append(CultureInfo.InvariantCulture,
            $";gc0={GC.CollectionCount(0) - Math.Max(0, lastGc0)};gc1={GC.CollectionCount(1) - Math.Max(0, lastGc1)};gc2={GC.CollectionCount(2) - Math.Max(0, lastGc2)}");
        detail.Append(CultureInfo.InvariantCulture,
            $";brain-alloc-bytes={BrainSections.LastBrainAllocatedBytes};thread-alloc-bytes={threadAllocated}");
        detail.Append(CultureInfo.InvariantCulture,
            $";hostiles={senses.Threats.Threats.Count};projectiles={senses.Projectiles.Threats.Count};drops={senses.Loot.Pickups.Count}");
        detail.Append(CultureInfo.InvariantCulture,
            $";facts={facts?.Count ?? -1};light-sites={lightSites};candidates={brain.Course.Candidates.Count};orders-priced={brain.Course.LastSearch.Evaluated}");
        detail.Append(CultureInfo.InvariantCulture,
            $";deciding={(!brain.Course.Last.Settled ? "true" : "false")};pending-models={brain.Course.PendingModelQueries};reach-corners={senses.Reach.CornerCount};reach-tiles={senses.Reach.AnyCount}");
        detail.Append(";tree=");
        if (BrainSections.LastTick == Main.GameUpdateCount) BrainSections.AppendTree(detail);
        else detail.Append('-');
        return detail.ToString();
    }

    /// <summary>
    /// Keeps the frame-overrun window, and writes it when it closes.
    ///
    /// An overrun is an interval past one frame at sixty a second, which is a fact of the engine's
    /// fixed timestep rather than a threshold anybody chose. The window is what keeps a session whose
    /// every frame overruns from filling the sidecar with two thousand records saying one thing, and
    /// it carries both halves of what a reader needs: how many frames in the window overran, and the
    /// whole split of the worst of them, so a hitch can be attributed without opening the row.
    /// </summary>
    private static void ObserveFrameOverrun(double frameMs, double remainder, NPC npc)
    {
        if (frameMs > FrameCost.FrameMilliseconds)
        {
            overrunsInWindow++;
            if (frameMs > worstFrameInWindow)
            {
                worstFrameInWindow = frameMs;
                worstFrameSplit = FormattableString.Invariant(
                    $"brain={lastBrainMs:0.00};record={(double.IsNaN(lastRecordMs) ? 0d : lastRecordMs):0.00};overlay={FrameCost.OverlayMilliseconds:0.00};inspector={FrameCost.InspectorMilliseconds:0.00};engine={remainder:0.00};draws={FrameCost.Draws}");
            }
        }
        if (Main.GameUpdateCount - overrunWindowOpenedAt < OverrunWindowTicks) return;
        if (overrunsInWindow > 0)
            GodsEyeEvents.RecordFrameOverrun(npc, overrunsInWindow, OverrunWindowTicks, worstFrameInWindow, worstFrameSplit);
        overrunWindowOpenedAt = Main.GameUpdateCount;
        overrunsInWindow = 0;
        worstFrameInWindow = 0;
        worstFrameSplit = "";
    }

    /// <summary>
    /// One extra preamble line from a harness that is driving this recorder rather than a game.
    ///
    /// It exists because the world run had to reach `QueueDiagnosticRecords.TryEnqueueTsv` by
    /// reflection to stamp what it was replaying, and a reflective call into a method this folder is
    /// free to rename is a break nobody finds until it throws at run time. The caller owns the line's
    /// content and this owns the `# ` and the ordering; it is refused once the header has been written,
    /// because a preamble line after the header is a line every reader will mis-parse as a trailer.
    /// </summary>
    /// <returns>Whether the line was accepted: false where no session is open or the header has already
    /// gone out, both of which are the caller's mistake rather than a dropped record.</returns>
    public static bool AnnotateHeader(string line)
    {
        if (diagnosticWriter == null || headerWritten || string.IsNullOrWhiteSpace(line)) return false;
        return QueueDiagnosticRecords.TryEnqueueTsv(line.StartsWith("# ", StringComparison.Ordinal) ? line : "# " + line);
    }

    private static void WriteMetadata()
    {
        if (diagnosticWriter == null)
            return;
        QueueDiagnosticRecords.TryEnqueueTsv($"# schema={Schema}");
        QueueDiagnosticRecords.TryEnqueueTsv($"# started_utc={sessionStartedUtc:O}");
        QueueDiagnosticRecords.TryEnqueueTsv($"# terraria={Main.versionNumber};tml_assembly={typeof(Main).Assembly.GetName().Version};runtime={Environment.Version};os={Environment.OSVersion.Platform}");
        QueueDiagnosticRecords.TryEnqueueTsv("# mods=" + string.Join(";", (ModLoader.Mods ?? Array.Empty<Mod>()).Select(mod => mod.Name + "@" + mod.Version)));
        QueueDiagnosticRecords.TryEnqueueTsv($"# source_revision={SourceProvenance}");
        QueueDiagnosticRecords.TryEnqueueTsv($"# capabilities={DescribeCapabilities()}");
        QueueDiagnosticRecords.TryEnqueueTsv($"# world={DescribeWorld()}");
        // `# config=` is deliberately *not* written here, and the reason is the whole of the 0.45.0
        // honesty fix: this runs at `OnWorldLoad`, which is before the character's saved preferences
        // have been loaded, so every value it could read is a default. The 22 September 2026 capture's
        // header said `chopping=Opportunistic` while its tick-1 `configuration` occurrence and every
        // census on every row said `Mimic`, and one of the two had to be lying to the reader. The line
        // is written from the first recorded row instead, which is the first moment the preferences the
        // brain is actually deciding with exist; it is still a preamble line, because the header itself
        // is written on that same row.
        // What a capture keeps and what it forgets, read from the constants that bound each store, so a reader can tell an
        // absence the recorder never kept from one that did not happen without knowing the code.
        QueueDiagnosticRecords.TryEnqueueTsv("# retention=rows=one-per-companion-ai-tick;events=every-occurrence-offered"
            + $";terrain-snapshots-remembered={RecordTerrainChunks.MaximumRemembered};terrain-captures-per-tick={RecordTerrainChunks.CapturesPerTick}"
            + $";recent-attempt-outcomes={Infrastructure.Selection.OwnCurrentActivity.RecentAttemptCapacity};cargo-transfer-ledger={(global::AICompanion.Companion.Inventory.CompanionInventory.RecentTransferCapacity)}"
            + $";cosmetic-contacts-per-summary={GodsEyeEvents.CosmeticContactsPerSummary};inspector-traces={BrainInspectorSamples.Capacity};inspector-cost-ticks={BrainInspectorSamples.CostTicks};session-map-tiles={SessionMap.MaxTilesRemembered}"
            + $";plan-dump-every-ticks={DumpEveryTicks};flush-every-ticks={FlushEveryTicks}");
        QueueDiagnosticRecords.TryEnqueueTsv("# lifecycle=world-entry-observed;tag-load-not-yet-observed;first-update-not-yet-observed;outer-load-unobservable;save-not-observed");
    }

    /// <summary>
    /// What each body could do, recorded rather than inferred.
    ///
    /// The distinction is the whole reason this line exists. A harness that watches where the
    /// player went and decides which ability must have taken them there is a heuristic with its own
    /// false positives, running underneath the thing being measured — and the question it feeds is
    /// exactly the one that matters: a place the player reached by wings is not evidence that the
    /// companion failed to reach it, and a place they walked to is. So both kits are written down
    /// at the source, and a checkpoint the companion's kit cannot express becomes a skipped row
    /// with its reason instead of a failure.
    ///
    /// The companion's side is <see cref="MovementCapabilities.Basic"/> — the mod's own declaration
    /// of the shipping kit, rather than a list of flags copied out of it, so an ability added to
    /// that record appears here by having been added there. It is the declared kit and not a live
    /// read: the header is written at world entry, before any companion exists to ask, and the
    /// navigator's own capabilities are an instance property with no instance yet. That is exact
    /// today, because mastery is unbuilt and every companion runs the basic kit; the moment a
    /// per-character kit can differ from the declaration, this line has to move to the row rather
    /// than stay in the header, which is also where the plan wants the player's flags.
    /// </summary>
    private static string DescribeCapabilities()
    {
        Player player = Main.LocalPlayer;
        return "companion:body=flying-orb,fly=True,liquids=air"
            + $";player:mount={player.mount?.Active == true},wings={player.wingsLogic > 0},dash={player.dashType},rocket-boots={player.rocketBoots}";
    }

    /// <summary>
    /// Which world this is, so two captures of one world can be compared and two captures of
    /// different worlds cannot be mistaken for one.
    ///
    /// What it distinguishes is worlds, and only worlds: without it, replaying a capture against a
    /// different world entirely is a false positive nobody would catch, because the route is the
    /// same, the terrain is not, and every unreachable answer reads as a regression.
    ///
    /// It deliberately does not distinguish one world from itself later. The hash is taken over the
    /// world's identity and seed, both of which survive every tile the player ever breaks, so a
    /// capture replayed against the same world after a night of mining matches on this field and is
    /// still being replayed against terrain that has moved. Catching that needs something derived
    /// from the tiles, which this is not, and a reader who takes the match as proof the terrain is
    /// unchanged has been told so by this comment rather than by the code.
    ///
    /// The hash is FNV-1a over the world's unique id and its generation seed, written out by hand
    /// for one reason that is easy to get wrong: <see cref="string.GetHashCode()"/> is randomised
    /// per process on .NET Core, so a hash taken from it would differ between two captures of the
    /// same world and agree with nothing, including itself tomorrow. A stable hash has to be one
    /// whose arithmetic is written down.
    /// </summary>
    private static string DescribeWorld()
    {
        string identity;
        try
        {
            var file = Main.ActiveWorldFileData;
            identity = file == null ? "unknown" : $"{file.UniqueId}|{file.Seed}";
        }
        catch (Exception)
        {
            // A world whose file data is not yet attached is unknown rather than a hash of nothing;
            // an invented identity would make two different worlds compare as one.
            identity = "unknown";
        }
        string hash = identity == "unknown" ? "unknown" : StableHash(identity);
        return $"id={Main.worldID};name={Main.worldName};hash={hash};size={Main.maxTilesX}x{Main.maxTilesY}";
    }

    /// <summary>FNV-1a, 64-bit, so the same input gives the same answer in every process and every run.</summary>
    private static string StableHash(string text)
    {
        ulong hash = 14695981039346656037UL;
        foreach (char c in text)
        {
            hash ^= c;
            hash *= 1099511628211UL;
        }
        return hash.ToString("x16", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// One whole-session artefact to its own file. It runs on world unload, where a throw would
    /// take the unload with it, so a failure to write a report is logged and swallowed: the report
    /// is a convenience and the session's own record is already on disk.
    /// </summary>
    private static void WriteWhole(string? path, Func<string> produce, string what)
    {
        if (path == null || diagnosticWriter == null)
            return;
        try
        {
            File.WriteAllText(path, produce(), Encoding.UTF8);
            ModContent.GetInstance<AICompanion>().Logger.Info($"BrainTelemetry: wrote the {what} to {path}");
        }
        catch (Exception e)
        {
            ModContent.GetInstance<AICompanion>().Logger.Warn($"BrainTelemetry: could not write the {what}: {e.Message}");
        }
    }

    private const int DumpEveryTicks = 300;
    // The window is wider than the screen on purpose, by Caner's ruling on 2026-09-08: the
    // replay treats the window's edge as a wall, so a route that leaves the box reads as "no
    // path", and the first dumps (six-tile pad) and the fifth run's (twenty) both had to be
    // widened from the saved world before they said anything. A screen is about 120 by 70
    // tiles at normal zoom; the pad alone is more than half of that on every side, so an
    // alternate route the companion never took is in the picture too. A block costs about
    // a byte per tile, so a run of sixty dumps is a few megabytes of git-ignored text.
    private const int DumpMaxWidth = 480, DumpMaxHeight = 400, DumpPad = 80;
    private static string? plansPath;
    private static long lastDumpTick;

    /// <summary>
    /// Write the tile window between a failed plan's start and its goal to a sidecar text
    /// file beside the session's telemetry, a few seconds apart at most, so the link the grid
    /// is missing can be read off the terrain after the run instead of guessed at. One
    /// character per tile: # solid, = platform or half block, ~ water or honey, L lava,
    /// o air the body can stand in, . other air; then S start, G goal, E where a partial
    /// path ends, N the companion's feet, P the player's feet on top.
    /// </summary>
    public static void DumpPlan(Point start, Point goal, Point? partialEnd, int expansions, string why)
    {
        if (plansPath == null || Main.GameUpdateCount - lastDumpTick < DumpEveryTicks)
            return;
        lastDumpTick = Main.GameUpdateCount;
        WriteWindow(start, goal, partialEnd, expansions, why);
    }

    /// <summary>
    /// The same window, written by a scenario detector (<see cref="ScenarioCapture"/>) under its
    /// own reason and its own cooldown, so a follow failure or a stuck run becomes a replayable
    /// block whether or not a plan failed in the same second.
    /// </summary>
    public static void DumpScenario(Point start, Point goal, string why)
    {
        if (plansPath == null)
            return;
        WriteWindow(start, goal, null, 0, why);
    }

    private static void WriteWindow(Point start, Point goal, Point? partialEnd, int expansions, string why)
    {
        // Read once and check here rather than trusting the caller's check: both callers do check,
        // but the field is a mutable static that world unload clears, so the check has to be in the
        // scope that uses it for the write to be sound as well as for the compiler to agree.
        string? path = plansPath;
        if (path == null)
            return;
        try
        {
            NPC? npc = CompanionNPC.Find();
            Point n = npc == null ? new Point(-1, -1) : MovementQueries.Tile(npc.Center);
            Point p = MovementQueries.Tile(Main.LocalPlayer.Bottom);

            // The window holds the player's tile as well as the start and the goal, because "could
            // it have reached the player" is the question the replay tool answers, and a goal the
            // positioner picked above a pit says nothing about the player six rows below the window.
            int x0 = Math.Min(Math.Min(start.X, goal.X), p.X) - DumpPad, x1 = Math.Max(Math.Max(start.X, goal.X), p.X) + DumpPad;
            int y0 = Math.Min(Math.Min(start.Y, goal.Y), p.Y) - DumpPad, y1 = Math.Max(Math.Max(start.Y, goal.Y), p.Y) + DumpPad;
            // A window too big to read is cut to the start's side, because the first missing link is near it.
            if (x1 - x0 >= DumpMaxWidth) { if (goal.X > start.X) x1 = x0 + DumpMaxWidth - 1; else x0 = x1 - DumpMaxWidth + 1; }
            if (y1 - y0 >= DumpMaxHeight) { if (goal.Y > start.Y) y1 = y0 + DumpMaxHeight - 1; else y0 = y1 - DumpMaxHeight + 1; }

            // The body is a rectangle at a pixel, and a tile is not enough to reproduce it. A tile
            // names a column and the grid proves a move at one of nine sub-tile offsets inside it,
            // so a dump that records only the tile is replayed from whichever offset the grid picks
            // rather than the one the body was actually at — which is how a fall-through that welds
            // the body into a wall at left 55795 replays clean from left 55788 (2026-09-09, the
            // 3487,362 shaft). Every question about clearance is asked of these four numbers.
            float boxLeft = npc == null ? 0f : npc.position.X;
            float boxTop = npc == null ? 0f : npc.position.Y;
            int boxW = npc?.width ?? 0, boxH = npc?.height ?? 0;
            int bx0 = (int)MathF.Floor(boxLeft / 16f), bx1 = (int)MathF.Floor((boxLeft + boxW - 0.01f) / 16f);
            int by0 = (int)MathF.Floor(boxTop / 16f), by1 = (int)MathF.Floor((boxTop + boxH - 0.01f) / 16f);

            var sb = new StringBuilder((x1 - x0 + 2) * (y1 - y0 + 1) + 200);
            sb.Append($"tick {Main.GameUpdateCount} {why}: start {start.X},{start.Y} goal {goal.X},{goal.Y}");
            if (partialEnd is Point e) sb.Append($" partial-end {e.X},{e.Y}");
            sb.Append($" expansions {expansions} npc {n.X},{n.Y}");
            // The orb's centre in pixels, which is the whole of the body: the contact is a circle about
            // this point, so a replay that starts the body here starts it where the game had it.
            if (npc != null)
                sb.Append($" orb {npc.Center.X.ToString("0.0", CultureInfo.InvariantCulture)},{npc.Center.Y.ToString("0.0", CultureInfo.InvariantCulture)}");
            sb.Append($" player {p.X},{p.Y} window x {x0}..{x1} y {y0}..{y1}\n");
            sb.Append($"markers S {start.X},{start.Y} G {goal.X},{goal.Y} N {n.X},{n.Y} P {p.X},{p.Y}");
            if (partialEnd is Point pe) sb.Append($" E {pe.X},{pe.Y}");
            sb.Append('\n');
            // The player's trail is the design's own pass line ("if I can get through, it can"),
            // written as one line the replay tool reads back and checks tile by tile.
            if (npc is NPC body && body.ModNPC is CompanionNPC companion && companion.Brain.Senses.Player.Trail.Count > 0)
            {
                sb.Append("trail");
                foreach (Point t in companion.Brain.Senses.Player.Trail)
                    sb.Append(' ').Append(t.X).Append(',').Append(t.Y);
                sb.Append('\n');
            }
            for (int y = y0; y <= y1; y++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    var t = new Point(x, y);
                    // The replay tool reads this alphabet back through the same function, so a
                    // slope or half block dumped here is the shape the offline planner sees. A
                    // marker is drawn over air only: on a half block or a floor slope the feet
                    // tile is the supporting tile itself, and a marker written there erased the
                    // support from the replay. The markers line above carries every position.
                    char c = TextTileWorld.Glyph(MovementQueries.World.Shape(x, y), MovementQueries.IsLiquid(x, y), MovementQueries.IsLava(x, y), MovementQueries.World.PassThrough(x, y));
                    if (c == '.' && MovementQueries.IsHoverable(t)) c = 'o';
                    if (c is '.' or 'o')
                    {
                        if (t == p) c = 'P';
                        else if (t == n) c = 'N';
                        else if (t == start) c = 'S';
                        else if (t == goal) c = 'G';
                        else if (partialEnd == t) c = 'E';
                        // The rest of the body, lowest priority so no other marker is lost to it,
                        // and lowercase so the marker reader still finds exactly one N. A body
                        // drawn as one glyph reads as a point that fits anywhere; drawn as the
                        // tiles its rectangle overlaps it reads as the two-wide, three-tall thing
                        // that has to fit through the gap, which is what the map is for.
                        else if (x >= bx0 && x <= bx1 && y >= by0 && y <= by1) c = 'n';
                    }
                    sb.Append(c);
                }
                sb.Append('\n');
            }
            sb.Append('\n');
            File.AppendAllText(path, sb.ToString());
        }
        catch (Exception e)
        {
            ModContent.GetInstance<AICompanion>().Logger.Warn($"BrainTelemetry.WriteWindow ({why}): {e.Message}");
        }
    }

    /// <summary>
    /// Write this tick's line after the brain selected and applied controls. The row carries the
    /// motor's AI-entry observation separately from the legacy after-helper NPC pose, because
    /// helpers may write position before this method runs.
    /// </summary>
    public static void Record(CompanionNPC companion)
    {
        if (!DiagnosticsConfiguration.CompanionDiagnosticsConfig.Current.RecordTelemetry) { if (diagnosticWriter != null) Close("recording-disabled"); return; }
        if (diagnosticWriter == null)
            return;
        long recordStarted = Stopwatch.GetTimestamp();
        // The recorder's own section opens after its clock starts and closes before its clock is read, so the
        // `record` subtree of the next tick's snapshot never exceeds this row's `record_ms`.
        var recording = BrainSections.Enter(RecordSection);
        var configuration = RecordedConfiguration.Current();
        // The first recorded row *establishes* the session's configuration rather than changing it, so
        // it writes no occurrence: the header line is written from this same value a few hundred lines
        // below, and a change record on the tick that sets the baseline would report every session as
        // having reconfigured itself on tick one. From the second row on, a difference is a preference
        // the player moved mid-session and is recorded as one.
        if (!headerWritten) recordedConfiguration = configuration;
        else if (configuration != recordedConfiguration)
        {
            recordedConfiguration = configuration;
            GodsEyeEvents.RecordConfiguration(configuration.Describe());
        }
        ScenarioCapture.Watch(companion);
        TravelEpisodes.Watch(companion);
        Brain brain = companion.Brain;
        var senses = brain.Senses;
        WatchSightedDrops(senses);
        NPC npc = companion.NPC;
        RecordTerrainChunks.ObserveActors(npc, Main.LocalPlayer);
        string decision = brain.Reflexes.Active ?? brain.LastAction?.Name ?? "-";
        bool brainExecuted = brain.LastTick == Main.GameUpdateCount;
        ObserveCostSpike(brain, npc, brainExecuted);
        var occurrences = BrainSections.Enter(RecordOccurrencesSection);
        bool choiceEvaluated = brainExecuted && brain.ChoiceEvaluated;
        var activity = brain.Activity;
        GodsEyeEvents.RecordActivity(npc, activity.Id, activity.Current?.Name ?? "none", activity.Phase.ToString(), activity.Reason,
            activity.LastEndedId, activity.LastEndReason, activity.ChangedAt);
        foreach (var outcome in activity.RecentAttempts)
            GodsEyeEvents.RecordAttemptOutcome(npc, outcome.AttemptId, outcome.ActivityId, outcome.Activity, outcome.Family.ToString(),
                outcome.StartTick, outcome.EndTick, outcome.Status.ToString(), outcome.Cause, outcome.ProductiveEffects,
                outcome.Attribution.ToString(), outcome.ClaimedYieldType, outcome.ClaimedYieldQuantity);
        var controlGrant = brain.ControlGrants.Last;
        bool controlFresh = controlGrant?.Tick == Main.GameUpdateCount;
        if (controlFresh && controlGrant is { } freshGrant)
            GodsEyeEvents.RecordControlGrant(npc, freshGrant.Id, freshGrant.Tick, freshGrant.ActivityId, freshGrant.ActivityPhase.ToString(),
                freshGrant.RequestedOwner, freshGrant.AppliedOwner, DescribeControls(freshGrant.RequestedMovement),
                DescribeControls(freshGrant.AppliedMovement), freshGrant.Hand.ToString(), freshGrant.AppliedVelocity, freshGrant.MotorApplications,
                freshGrant.AttemptId);
        string controls = DescribeControls(companion.Motor.AppliedControls);
        string activityControls = controls + $";activity-id={activity.Id};activity-phase={activity.Phase};activity-reason={activity.Reason}"
            + ";gravity-observation-tick=-1;engine-gravity=0;model-gravity=0;gravity-enabled=False";
        var combat = default(Activities.Combat.FightEnemies);
        var mine = default(Activities.Gathering.MineOre);
        var chop = default(Activities.Gathering.ChopTree);
        var light = default(Activities.NearbyAssistance.LightUsefulArea);
        foreach (var candidate in brain.Actions)
        {
            if (candidate is Activities.NearbyAssistance.LightUsefulArea lightAction) light = lightAction;
            if (candidate is Activities.Combat.FightEnemies combatAction) combat = combatAction;
            if (candidate is Activities.Gathering.MineOre mineAction) mine = mineAction;
            if (candidate is Activities.Gathering.ChopTree chopAction) chop = chopAction;
        }
        activityControls += $";mine-last-conclusion={mine?.LastConclusion?.ToString() ?? "none"}";
        // The one contract that is a per-tick cost rather than a property of a decision, so it is
        // audited here: `DecideMs` is laid at the end of the decide phase and does not exist at the
        // moment the decision records itself. Every other contract is audited where the decision is
        // recorded, in `AuditDecisionContracts`.
        AuditDecisionContracts.ObserveDecideCost(brain.DecideMs, (long)Main.GameUpdateCount);
        if (decision != lastDecision || Main.GameUpdateCount % 60 == 0)
        {
            var board = new StringBuilder();
            // The board opened with the family chooser's per-activity score list, its nine-factor
            // breakdown per activity and its three family nominations. All three went with the chooser
            // in schema 0.46.0, and all three had been empty since `0bb2c8a`: what replaced them is the
            // course's own three questions below, which `0.42.0` added because the occurrence had stopped
            // naming anything that lost.
            board.Append(CultureInfo.InvariantCulture, $"regroup={brain.Companionship.RegroupUrgency:0.000};return-ticks={brain.Companionship.EstimatedReturnTicks:0.0}");
            // The course's own alternatives, which replaced the family nominations rather than
            // joining them: the tick asks a course, so the chooser named nothing that lost.
            // A record that says only what happened
            // answers "what happened"; this folder's standing rule is to record the rejected option and
            // the reason for the negative, which for a course means three different questions.
            //
            // `course` is what the best order led by each domain scored, with the terms that decide it,
            // so a reader can tell a job that lost narrowly from one worth nothing. `course-admitted` is
            // how much of each census the search could actually order — a domain can report a complete
            // sweep and contribute nothing, because only a usable candidate enters an order at all, and
            // the two read identically without this. `course-refused` is why orders were thrown away,
            // which is the number that was previously recorded as a bare count nobody could act on.
            var course = brain.Course;
            board.Append(CultureInfo.InvariantCulture,
                $";course-decision={course.Last.Reason},activity={course.Last.Activity},settled={course.Last.Settled}"
                + $",steps={course.Course.Current?.Projection.Steps.Count ?? -1}"
                + $",priced={course.LastSearch.Evaluated},refused={course.LastSearch.Rejected},exhausted={course.LastSearch.Exhausted}");
            foreach (var leader in course.LastLeaders.OrderByDescending(entry => entry.Value.Total.Nominal))
                board.Append(CultureInfo.InvariantCulture,
                    $";course:{leader.Key}=value:{leader.Value.Total.Nominal:0.000},useful:{leader.Value.UsefulEffects:0.000}"
                    + $",harm:{leader.Value.Harm:0.000},gap:{leader.Value.Companionship:0.000}");
            // The reason here is the commonest *non-usable* admission's, which merges two groups that
            // fail in opposite directions: a candidate proven unusable is a census that finished and
            // found nothing, and one left unresolved is a census that has not answered, which is the
            // middle value this whole tree is built on not collapsing. A domain with both reports one
            // string and a reader cannot tell which group it describes.
            //
            // **The group is one accessor away and it is on the course's own branch.**
            // `DecideCourseEachTick.Candidates` exposes each `Opportunity` with its `Admission` and its
            // `Reason`, so the reason can be tagged with the group it came from — a stale candidate
            // reads `Unresolved:admission-evidence-absent` rather than a bare reason string. The line is
            // written out and commented because this worktree does not carry that accessor yet; it is
            // uncommented at the merge and the plain `reason:` below goes with it.
            //
            //   string Group(string domain) => course.Candidates
            //       .Where(c => c.Key.Domain == domain && c.Admission != OpportunityAdmission.KnownUsable)
            //       .GroupBy(c => c.Admission + ":" + c.Reason, StringComparer.Ordinal)
            //       .OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal)
            //       .Select(g => g.Key).FirstOrDefault() ?? "-";
            //
            // `container-usable` (0.47.0) is how many of a domain's usable candidates are containers — a pot,
            // an opportunity with unknown contents — counted by the need the candidate carries rather than by
            // the domain it sits in, because the owner ruled on 23 September 2026 that pots are a collection
            // task and moved them from `pot-target` into `collect-target`, where the domain alone no longer
            // tells a pot from a drop. Read from the need kind, the count is the same whichever domain
            // publishes the pot, so the witness reading it survives the move in either direction.
            var containers = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var candidate in course.Candidates)
            {
                if (candidate.Admission != Selection.Opportunities.OpportunityAdmission.KnownUsable) continue;
                foreach (var need in candidate.Needs)
                {
                    if (need.Key.Kind != Selection.Opportunities.NeedKind.Container) continue;
                    containers[candidate.Key.Domain] = containers.TryGetValue(candidate.Key.Domain, out int had) ? had + 1 : 1;
                    break;
                }
            }
            foreach (var domain in course.Admitted)
                board.Append(CultureInfo.InvariantCulture,
                    $";course-admitted:{domain.Domain}=usable:{domain.Usable},unknown:{domain.Unresolved},unusable:{domain.Unusable},reason:{(domain.Reason.Length == 0 ? "-" : domain.Reason)}"
                    + $",container-usable:{(containers.TryGetValue(domain.Domain, out int pots) ? pots : 0)}");
            foreach (var refusal in course.LastRefusals.OrderByDescending(entry => entry.Value))
                board.Append(CultureInfo.InvariantCulture, $";course-refused:{refusal.Key}={refusal.Value}");
            var preferences = PlayerIntegration.CompanionPreferences.Current;
            board.Append(CultureInfo.InvariantCulture, $";movement-stalled={brain.MovementStalled};activity-status={brain.ActivityStatus};activity-target={brain.LastAction?.ActivityTarget};activity-radius={preferences.NewActivityRadius};continuation-radius={preferences.ActiveActivityRadius};recovery-radius={preferences.RecoveryRadius}");
            GodsEyeEvents.RecordDecision(npc, brain.LastAction?.Name ?? "-", board.ToString(), brain.LastRequest.Kind.ToString(),
                activityControls + $";freshness={(choiceEvaluated ? "fresh" : "stale-or-not-executed")};brain-fresh={brainExecuted};choice-id={brain.Course.DecisionId};choice-tick={brain.Course.DecisionTick?.ToString(CultureInfo.InvariantCulture) ?? "unavailable"};execution={decision};control-source={companion.Motor.ControlSource}");
            lastDecision = decision;
        }
        occurrences.Dispose();
        GodsEyeEvents.RecordMovementState(npc, brain.Navigator);
        GodsEyeEvents.RecordNavigationEvidence(npc, brainExecuted, decision, brain.LastRequest.Kind.ToString(), activityControls,
            brain.Navigator.SearchId, brain.Navigator.AttemptId, brain.Navigator.SearchExpansions, brain.Navigator.SearchPending,
            brain.Navigator.ProgressReason, 0,
            brain.Positioner.CandidateCount, brain.Positioner.ReachableCandidateCount, brain.Positioner.RejectedCandidateCount, brain.Positioner.ChoiceReason,
            senses.Threats.InterventionTicks, senses.Threats.ProtectionUrgency,
            senses.Threats.MostUrgent?.PredictionConfidence ?? 0f, senses.Threats.MostUrgent?.PredictionSamples ?? 0,
            $"route-completed-steps={brain.Navigator.Path?.Index ?? 0};route-remaining-estimated-ticks={brain.Navigator.RemainingEstimatedRouteTicks:0.000};follow-objective-valid={brain.Positioner.FollowObjectiveSatisfied};follow-horizontal-gap={brain.Positioner.FollowHorizontalGap:0.000};follow-vertical-gap={brain.Positioner.FollowVerticalGap:0.000};follow-objective={brain.Positioner.FollowObjectiveReason};recovery-active={brain.FollowRecovery.Active};recovery-reason={brain.FollowRecovery.Reason};recovery-flights={brain.FollowRecovery.Flights};plan-id={combat?.OfferedPlan?.Id ?? -1};plan-segment={combat?.OfferedSegment ?? -1};plan-stand={PlanStand(combat)};mine-job={mine?.JobId ?? 0};mine-policy={mine?.Policy.ToString() ?? "unavailable"};mine-status={mine?.Status ?? "unavailable"};mine-remaining={mine?.RemainingTiles ?? 0};mine-target={mine?.TargetTile?.ToString() ?? "-"};control-source={companion.Motor.ControlSource};position-evidence-tick={brain.Positioner.EvidenceTick};positions-evaluated={brain.Positioner.EvaluatedCandidates};reach-complete={senses.Reach.Complete};position-alternatives={brain.Positioner.CandidateEvidence};plan-invalid={companion.Combat.Planner.LastInvalidation};plan-value={(combat?.OfferedPlan?.Weighted ?? 0f).ToString("0.000", CultureInfo.InvariantCulture)};plan-front={combat?.OfferedFrontSize ?? 0}");
        SessionMap.Watch(
            MovementQueries.Tile(npc.Center),
            MovementQueries.Tile(senses.Player.Bottom),
            brain.Navigator.GoalTile,
            brain.Positioner.Chosen is Vector2 spot ? Vector2.Distance(npc.Center, spot) : float.MaxValue);

        if (!headerWritten)
        {
            // The configuration the session is actually running under, read at the first recorded row
            // rather than at world entry, because the character's saved preferences load between the
            // two. Reading it here is what makes the header and the tick-1 `configuration` occurrence
            // the same answer; before 0.45.0 they could disagree and the capture gave a reader no way
            // to tell which was the lie. Enqueued before the header, so it is still a preamble line.
            QueueDiagnosticRecords.TryEnqueueTsv($"# config={recordedConfiguration.Describe()}");
            var textColumns = new StringBuilder("# text_columns=state,action,reflex,top_threat,target,request,anchor,spot,lookahead,npc_tile,npc_px,npc_vel,wall_normal,liquid,held,weapon,fire,engage,torch,player_tile,spot_home,sample_phase,player_px,player_vel,player_liquid,player_hit,npc_hit,player_state,player_activity,player_support,control,control_source,desired_vel,follow_reason,recovery_reason,plan_stand,mine_policy,mine_status,mine_target,plan_invalid,nav_status,position_reason,plan_reason,hand_grant,control_request_owner,collection_method,mine_end_reason,attempt_end_activity,attempt_end_family,attempt_end_status,attempt_end_cause,attempt_end_attribution,plan_targets,plan_uses,aim_target,landed_hit_target,landed_hit_aimed,encounter_source,torch_reason,lighting_sites,intent_region,task_order,task_order_runner_up,evade_reason,evade_choice,plan_vector,knowledge_residual");
            // Offer columns are named from the registered activities, like the raw/final pairs, so
            // the declaration and the header cannot disagree about which activities exist.
            foreach (var a in brain.Actions) textColumns.Append(',').Append(a.Name).Append("_offer");
            textColumns.Append(",meeting_reason,meeting_anchor,meeting_flood");
            textColumns.Append(",nav_failure,nav_failure_reason,nav_attempt_ending");
            textColumns.Append(",region_kind,region_anchor_px,region_player_px,region_comfort,region_work_tile,region_reach,region_arrival");
            textColumns.Append(",torch_reference,torch_reference_dark,torch_reference_stage");
            // 0.45.0's two textual columns. The declaration is a hand-maintained string beside the
            // header builder and is the half that gets forgotten, which is how `torch_reason` spent a
            // whole schema reading as a column that failed to parse as a number.
            textColumns.Append(",target_evidence,target_evidence_age");
            // 0.48.0's one textual column.
            textColumns.Append(",sections,alloc_sections");
            QueueDiagnosticRecords.TryEnqueueTsv(textColumns.ToString());
            var h = new StringBuilder();
            // A start timestamp is file metadata. Stopwatch is the observed wall duration of
            // every row; deriving wall time from game ticks would conceal pauses and lag.
            h.Append("tick\tstate\taction\treflex");
            foreach (var a in brain.Actions)
                h.Append('\t').Append(a.Name).Append("_raw\t").Append(a.Name).Append("_fin");
            h.Append("\tdanger\tself_threat\thorizon\tthreats\treachable\ttop_threat\ttarget\tloot");
            // The route as the navigator holds it: how many points it has, which segment the body is on,
            // the length left to fly and where the steering is aiming this tick. A route that shrinks
            // while the body does not move is a body pinned on a route it cannot keep.
            h.Append("\trequest\tanchor\tspot\tspot_score\tfollow_objective_valid\tfollow_dx\tfollow_dy\tfollow_reason\trecovery_active\trecovery_reason\trecovery_flights\troute_points\troute_index\troute_search_id\troute_attempt_id\troute_remaining_ticks\troute_remaining_px\tlookahead\tplan_failed\texpansions");
            // The body: its centre and velocity, whether the contact pushed it off a wall this tick and
            // along which normal, the liquid it touches, how far it is from the nearest wall, the engine's
            // own displacement, and how long it has held a velocity without moving. There is one body and
            // one contact, so there is no second body to diverge from and no ground to stand on: those
            // columns went with the walker, and the liquid's hurt and contact count went when every
            // liquid became air to the orb.
            h.Append("\tnpc_tile\tnpc_px\tnpc_vel\ttouched_wall\twall_normal\twet\tliquid\tclearance\tmoved\tpinned\tdir\tlife\tself_danger\theld\tweapon\tshot\tfire\tplan_value\tplan_front\tplan_cut\tnear_threat\tweapon_reach\tengage\ttorch\tdark_near\tdark_ahead\ttorch_reason\tlight_samples\tlight_read_tick\tlight_region");
            h.Append("\tplayer_tile\tplayer_intent\tplayer_dead\tplayer_attacking\tplayer_chopping\tplayer_mining");
            h.Append("\tplan_ms\tflood_ms\tsenses_ms\treflex_ms\tdecide_ms\tposition_ms\tnavigate_ms\tbrain_ms\tclearance_builds\tstranded");
            // The reachability tier, which is where the companion decides whether to enter somewhere
            // it cannot leave and the one decision no offline pass can watch: how many tiles it can
            // reach, how many of those it can come home from, whether the spot it picked is one of
            // them, and whether the refusing flood was discarded because the player was outside it.
            h.Append("\treach_any\treach_two_way\treach_complete\tspot_home\tplayer_one_way");
            h.Append("\tplan_id\tplan_segment\tplan_stand\tmine_job\tmine_policy\tmine_status\tmine_remaining\tmine_target\tplan_invalid\tknowledge_residual\tshot_error");
            // `control` is what the motor applied this tick and `desired_vel` the velocity it accelerated
            // toward after capping; the two differ where the request exceeded the cap.
            h.Append("\twall_elapsed_ms\tsample_phase\tplayer_px\tplayer_vel\tplayer_ground\tplayer_liquid\tplayer_life\tplayer_hit\tnpc_hit\tplayer_state\tplayer_activity\tplayer_support\tcontrol\tcontrol_source\tbrain_fresh\tdesired_vel");
            h.Append("\tnav_status\tposition_reason\tmovement_stalled\tplan_vector\tplan_kills\tplan_prevented\tweapon_cooldown\tplan_remaining\tplan_reason");
            h.Append("\tchoice_fresh\tchoice_id\tchoice_tick");
            h.Append("\tcontrol_grant_fresh\tcontrol_grant_id\tcontrol_grant_tick\thand_grant\tcontrol_request_owner\tcontrol_motor_applications\tfinalise_ms");
            h.Append("\tplayer_intent_y\tplayer_intent_confidence\tplayer_intent_samples\tplayer_local_work_fraction");
            h.Append("\tmine_remaining_work_ticks\tmine_remaining_hits\tchop_remaining_work_ticks\tchop_remaining_hits");
            h.Append("\treunion_apart_ticks\treunion_departure\treunion_delay_cost_per_tick");
            h.Append("\tcollection_method");
            h.Append("\tmine_end_job\tmine_end_tick\tmine_end_reason\tmine_end_tracked\tmine_end_present\tmine_end_changed\tmine_end_missing\tmine_end_unobserved\tmine_end_companion_removed_sites\tmine_end_observed_clear");
            h.Append("\tactivity_attempt_id\tattempt_end_id\tattempt_end_activity_id\tattempt_end_activity\tattempt_end_family\tattempt_end_status\tattempt_end_cause\tattempt_end_attribution\tattempt_end_effects\tattempt_end_start_tick\tattempt_end_tick");
            foreach (var a in brain.Actions)
                h.Append('\t').Append(a.Name).Append("_offer");
            h.Append("\tmeeting_reason\tmeeting_anchor\tmeeting_player_ticks\tmeeting_companion_ticks\tmeeting_candidates\tmeeting_priced\tmeeting_flood");
            h.Append("\tnav_failure\tnav_failure_reason\tnav_failure_search_id\tnav_failure_attempt_id\tnav_attempt_ending\tnav_attempts_completed\tnav_attempts_failed\tnav_attempts_preempted\tnav_attempts_cancelled");
            h.Append("\tplan_targets\tplan_dps\tplan_travel\tplan_uses\taim_target\tlanded_hit_target\tlanded_hit_aimed\tlanded_hit_damage\tlanded_hit_tick\tlanded_hits\tplan_kill_tick\tplan_threat_removed\ttop_threat_effective_player\ttop_threat_effective_companion\tencounter_intensity\tencounter_source\tencounter_recognised\tencounter_pressure_ticks\tplan_first_damage");
            // The success region the positioner admitted its destination against, and whether a claimed arrival lies
            // inside it. The region is the positioner's own snapshot from the resolve that admitted it, so the report
            // judges arrival against what the destination was chosen for, not against the world some ticks later.
            h.Append("\tregion_kind\tregion_revision\tregion_tick\tregion_terrain\tregion_anchor_px\tregion_player_px\tregion_comfort\tregion_work_tile\tregion_reach\tregion_arrival");
            // What recording costs and what the capture did not keep, as running totals: the previous row's Record cost,
            // occurrences handed to the event writer, refused after a failed write, and folded into summaries, and
            // terrain chunks the snapshot memory forgot. The end marker restates the closing totals.
            h.Append("\trecord_ms\tevents_written\tevents_dropped\tevents_coalesced\tterrain_evictions");
            // What travelling has cost so far, as running values rather than per-tick ones: stops against minutes of
            // route travel, and the mean observed speed over those same ticks. Both read -1 until the body has spent a
            // tick on a route, because a rate over no travel is not zero, it is unmeasured. The route-episode and stop
            // occurrences beside them carry the individual journeys; these two are the shape of the session.
            h.Append("\tstops_per_minute\troute_speed_mean");
            // What lighting's last discovery search actually asked and what the reach sense answered, as
            // `x,y=verdict` pairs, beside the number of sites it put to the query. Without them a starved
            // search and a search over a genuinely sealed screen produce the same offer string, which is how
            // 79% of the 2026-09-14 session could read "the question was not finished" with no way to tell
            // from the record whether three sites had been asked or three hundred, or whether the answers were
            // refusals or a flood that had not settled. The ledger is capped and the count is not, so the two
            // together read as "the first few of this many" rather than as the whole search.
            h.Append("\tlighting_sites\tlighting_sites_asked");
            // How many terrain edits the world has announced since it loaded. It is a running count
            // rather than a per-tick flag, so a reader takes differences: the edit *rate* is what
            // decides how often retained search work is discarded, and until this column existed no
            // capture could say what that rate was — the 426 terrain-snapshot records of the
            // 2026-09-14 session were captures on a cadence and were briefly read as edits. Appended
            // at the end with the schema left where it is, because nothing before it moved and every
            // reader addresses columns by name.
            h.Append("\tterrain_revision");
            // Lane C, appended at the end of the row so the other lanes' columns keep their index.
            // `follow_dx`/`follow_dy` change meaning rather than position in this version: they are
            // the offsets to the intent region's centre, not to the player's feet, and a check built
            // on their old meaning reads a lead as a following error. `intent_region` is the region
            // itself so a replay can redraw it; `intent_pull` is what keeping company priced its
            // reunion on, which is the number the never-overtakes defect was a flat zero of.
            h.Append("\tintent_region\tintent_pull");
            // The order the chooser put its close jobs in and the best order that started with a different job, each
            // as names joined by '>' with the order's score, or '-' when fewer than two jobs were close. Appended with
            // the schema left where it is: nothing before it moved and every reader addresses columns by name.
            h.Append("\ttask_order\ttask_order_runner_up");
            // Lane A, schema 0.35.0, appended at the end so every column before it keeps its index: the player's own
            // smart-cursor torch tile, its light, its placement reading and the stage lighting refuses it at, and this
            // tick's collections. `<activity>_time` and `<activity>_funnel` opened this block and went in 0.46.0.
            h.Append("\ttorch_reference\ttorch_reference_light\ttorch_reference_dark\ttorch_reference_stage\tgc0\tgc1\tgc2");
            lastGc0 = lastGc1 = lastGc2 = -1;
            // Lane C (the evade layer), appended after lane A's block: why the layer kept or bent this tick's controls, the
            // lookahead tick at which the job's own flight met a hit (-1 for never), which candidate a bent tick flew, and how
            // many candidates each refusal removed. `evade_reason` and `evade_choice` are textual and declared in the preamble.
            h.Append("\tevade_reason\tevade_hit_tick\tevade_choice\tevade_refused_nowhere\tevade_refused_danger");
            // Schema 0.45.0's frame ledger, appended at the end so every column before it keeps its index.
            // `frame_ms` is update-to-update and **an update is not a frame** — the engine catches up by
            // running two updates with no draw between them — so `draws` carries how many draws the
            // interval held and frames a second is derived from that rather than from the interval.
            // `overlay_ms` and `inspector_ms` are the two sections of this mod's own drawing, timed on the
            // ledger's own clock; `engine_ms` is what is left of the interval after the *previous* row's
            // brain, this row's `record_ms` (which is also the previous row's cost) and the two draw
            // sections, all of which are that same update's. The interval a row can see closed at the
            // end of the update before it, so `frame_ms` describes the update before the row's own.
            h.Append("\tframe_ms\tdraws\toverlay_ms\tinspector_ms\tengine_ms");
            // The two names return here, and **they do not mean what they meant before 0.40.0**, which
            // is why a reader of them names a 0.45.0-only witness column rather than trusting the name.
            // They were the arsenal's own rejected-pair shortlist — `slot:generation:0:0:weapon=N:reason`
            // — and went with the weapon block's rename; they are the course's now, per domain, as
            // `<domain>=<fact key>:<evidence>:<observed>/<total>` and `<domain>=<ticks>`, which is the
            // measurement six readings of the 22 September capture each named as the one thing that
            // would have settled the census-against-binder contradiction and could not be taken.
            h.Append("\ttarget_evidence\ttarget_evidence_age");
            // Pixels from the body's bottom edge down to the first support, platforms counted, negative
            // where the edge is already inside it. It ends the row because a body sinking into a
            // platform was otherwise a reconstruction from the centre, the grid and a radius.
            h.Append("\tsupport_below_px");
            // Schema 0.48.0: where the tick's time and memory went. `tick_alloc_bytes` is what the update thread
            // allocated since the previous row, the engine's share included; `brain_alloc_bytes` is the brain tick's
            // own; `sections` is the section profiler's largest self times this tick and `sections_other_ms` the rest
            // of them; `cost_fence_ms` is the fence this tick's `brain_ms` was judged against, `-` while the window
            // fills; `alloc_sections` names the sections that allocated the most outside their children. `gc0`..`gc2`
            // already carry the collections, since 0.35.0.
            h.Append("\ttick_alloc_bytes\tbrain_alloc_bytes\tsections\tsections_other_ms\tcost_fence_ms\talloc_sections");
            lastThreadAllocated = -1;
            QueueDiagnosticRecords.TryEnqueueTsv(h.ToString());
            headerWritten = true;
        }

        var row = BrainSections.Enter(RecordRowSection);
        var sb = new StringBuilder(400);
        sb.Append(Main.GameUpdateCount);
        // Read player death from the live player rather than a cached observation. The brain now
        // continues after death, but this remains the authoritative lifecycle state for the row.
        sb.Append('\t').Append(companion.IsDowned ? "downed" : Main.LocalPlayer.dead ? "player-dead" : "up");
        sb.Append('\t').Append(brain.LastAction?.Name ?? "-");
        sb.Append('\t').Append(brain.Reflexes.Active ?? "-");
        foreach (var a in brain.Actions)
        {
            (float raw, float fin) = CourseWorthOf(brain, a);
            sb.Append('\t').Append(raw.ToString("0.00")).Append('\t').Append(fin.ToString("0.00"));
        }

        int reachable = 0;
        Infrastructure.Observation.ThreatRecord? top = null;
        foreach (var t in senses.Threats.Threats)
        {
            if (t.CanReachPlayer) reachable++;
            if (top == null || t.Urgency > top.Urgency) top = t;
        }
        sb.Append('\t').Append(senses.Threats.PlayerDanger.ToString("0.00"));
        // Danger to the companion beside danger to the player, because the two came apart badly:
        // it died 84 tiles out with the player's danger reading 0.00 on every hit it took.
        sb.Append('\t').Append(senses.Threats.CompanionDanger.ToString("0.00"));
        sb.Append('\t').Append(senses.Threats.Horizon == float.MaxValue ? "inf" : senses.Threats.Horizon.ToString("0"));
        sb.Append('\t').Append(senses.Threats.Threats.Count).Append('\t').Append(reachable);
        sb.Append('\t').Append(top == null ? "-" : $"{top.Npc.TypeName}:{top.Class.ToString()[0]}:u{top.Urgency:0.00}:t{(top.TicksToPlayer > 9999 ? 9999 : (int)top.TicksToPlayer)}:{(top.CanReachPlayer ? "r" : "x")}{(top.Shoots ? ":s" : "")}");
        sb.Append('\t').Append(brain.LastRequest.Target is NPC target && target.active ? target.TypeName : "-");
        sb.Append('\t').Append(senses.Loot.Pickups.Count);

        sb.Append('\t').Append(brain.LastRequest.Kind);
        sb.Append('\t').Append(Tile(brain.LastRequest.Anchor));
        sb.Append('\t').Append(brain.Positioner.Chosen is Vector2 c ? Tile(c) : "-");
        sb.Append('\t').Append(brain.Positioner.ChosenScore.ToString("0.00"));
        sb.Append('\t').Append(brain.Positioner.FollowObjectiveSatisfied ? 1 : 0);
        sb.Append('\t').Append(brain.Positioner.FollowHorizontalGap.ToString("0.00", CultureInfo.InvariantCulture));
        sb.Append('\t').Append(brain.Positioner.FollowVerticalGap.ToString("0.00", CultureInfo.InvariantCulture));
        sb.Append('\t').Append(brain.Positioner.FollowObjectiveReason);
        sb.Append('\t').Append(brain.FollowRecovery.Active ? 1 : 0);
        sb.Append('\t').Append(brain.FollowRecovery.Reason);
        sb.Append('\t').Append(brain.FollowRecovery.Flights);
        Route? path = brain.Navigator.Path;
        sb.Append('\t').Append(path?.Count ?? 0).Append('\t').Append(path?.Index ?? 0);
        sb.Append('\t').Append(brain.Navigator.SearchId).Append('\t').Append(brain.Navigator.AttemptId);
        sb.Append('\t').Append(brain.Navigator.RemainingEstimatedRouteTicks.ToString("0.00", CultureInfo.InvariantCulture));
        sb.Append('\t').Append((path?.RemainingLength(npc.Center) ?? 0f).ToString("0.0", CultureInfo.InvariantCulture));
        sb.Append('\t').Append(path != null ? Pair(brain.Navigator.Lookahead) : "-");
        sb.Append('\t').Append(brain.Navigator.LastPlanFailed ? 1 : 0).Append('\t').Append(brain.Navigator.LastExpansions);

        sb.Append('\t').Append(Tile(npc.Center));
        sb.Append('\t').Append((int)npc.Center.X).Append(',').Append((int)npc.Center.Y);
        sb.Append('\t').Append(npc.velocity.X.ToString("0.00")).Append(',').Append(npc.velocity.Y.ToString("0.00"));
        // The contact's own account of the last application: whether it pushed the body off a wall
        // and along which normal. The engine's collide flags are always clear for a body it does not
        // collide, so this is the only wall contact the record has.
        sb.Append('\t').Append(companion.Motor.TouchedWall ? 1 : 0);
        sb.Append('\t').Append(companion.Motor.TouchedWall ? Pair(companion.Motor.WallNormal) : "-");
        sb.Append('\t').Append(npc.wet ? 1 : 0);
        sb.Append('\t').Append(LiquidName(companion.Motor.LiquidKind));
        // How far the body's edge is from the nearest wall, in pixels, from the same circle test the
        // contact runs; zero is a body overlapping terrain, which recovery clearance exists for.
        sb.Append('\t').Append(CircleContact.Clearance(MovementQueries.World, npc.Center).ToString("0.0", CultureInfo.InvariantCulture));
        // What the engine did with the last tick's request. Record runs inside AI, before the engine's
        // position += velocity, so every number on this line describes the tick before. `moved` is
        // the engine's own displacement (position - oldPosition); a body with a velocity and moved
        // 0,0 is not being integrated, which is what `pinned` counts across ticks.
        sb.Append('\t').Append((npc.position.X - npc.oldPosition.X).ToString("0.00", CultureInfo.InvariantCulture))
          .Append(',').Append((npc.position.Y - npc.oldPosition.Y).ToString("0.00", CultureInfo.InvariantCulture));
        // How long the body has held a velocity while not moving. The engine cannot do that to a
        // body it is integrating, so any run above a tick or two means something wrote the
        // position back during the AI phase, where `moved` cannot see it.
        sb.Append('\t').Append(companion.Motor.PinnedTicks);
        sb.Append('\t').Append(npc.direction);
        sb.Append('\t').Append(npc.life).Append('/').Append(npc.lifeMax);
        sb.Append('\t').Append(senses.Self.SelfDanger.ToString("0.00")).Append(senses.Self.OnFire ? "f" : "");
        sb.Append('\t').Append(companion.HeldItemType == 0 ? "-" : Lang.GetItemNameValue(companion.HeldItemType));
        sb.Append('\t').Append(PlanWeaponName(combat, companion));
        sb.Append('\t').Append(companion.Combat.LastShotSolved ? 1 : 0);
        // Why no projectile left the hands, which the shot flag alone cannot say: a reload and a
        // plan with nothing worth firing both read as a zero there, and they want opposite fixes.
        sb.Append('\t').Append(companion.Combat.LastFireOutcome);
        // The offered plan's weighted value, the front it survived, and whether a cut search offered
        // nothing: a row where combat runs with no plan id beside it is a defect with no other tell.
        sb.Append('\t').Append((combat?.OfferedPlan?.Weighted ?? 0f).ToString("0.000", CultureInfo.InvariantCulture));
        sb.Append('\t').Append(combat?.OfferedFrontSize ?? 0);
        sb.Append('\t').Append(combat?.OfferedCut == true ? 1 : 0);
        // How far the nearest reachable hostile is, and how far the hands can actually throw,
        // both in tiles. These exist because "no target" is ambiguous without them and the
        // 2026-09-09 session could not be read: the hands reported no-target on 78.3% of ticks
        // with reachable hostiles present, and reachable in the threat sense means "a walker could
        // path from it to the player", which has nothing to do with being inside weapon range.
        // Without both numbers on the row, a companion correctly declining a shot it cannot make
        // and a companion failing to see a target it could hit are the same cell — and the first
        // is right behaviour, so guessing which one it is risks fixing a thing that is not broken.
        float nearest = float.MaxValue;
        foreach (Infrastructure.Observation.ThreatRecord t in senses.Threats.Threats)
            if (t.CanReachEither && t.Npc != null && t.Npc.active)
                nearest = MathF.Min(nearest, t.DistanceToCompanion);
        sb.Append('\t').Append(nearest == float.MaxValue ? "-" : (nearest / 16f).ToString("0.0", CultureInfo.InvariantCulture));
        sb.Append('\t').Append((companion.Combat.MaxReach / 16f).ToString("0.0", CultureInfo.InvariantCulture));
        // What the hands are shooting at, which is now independent of what the feet were told, so
        // "it was following me and not attacking" is a row where engage reads "-" beside threats.
        sb.Append('\t').Append(brain.EngageTarget is NPC eng && eng.active ? eng.TypeName : "-");
        sb.Append('\t').Append(companion.Torch.Shown ? "shown" : companion.Torch.Lit ? "lit-busy" : "out");
        // What the torch decision actually read: the share of measured open air that is dark around the
        // body and at the player's predicted feet, and the answer that decided the hand. A mean brightness
        // stood here before and could not tell a lit room inside a dark cave from a dim one, which is the
        // defect these three replace. An unmeasured neighbourhood writes "-", never a zero, because zero
        // dark and nobody looked are opposite facts.
        var darkNear = senses.Light.DarkAirNear(npc.Center.ToTileCoordinates(), Selection.Weights.TorchHoldRadiusTiles);
        var darkAhead = senses.Light.DarkAirNear(
            senses.Player.Predict(Selection.Weights.TorchHeadingLeadTicks).ToTileCoordinates(), Selection.Weights.TorchHoldRadiusTiles);
        sb.Append('\t').Append(darkNear.Unmeasured ? "-" : darkNear.DarkFraction.ToString("0.00", CultureInfo.InvariantCulture));
        sb.Append('\t').Append(darkAhead.Unmeasured ? "-" : darkAhead.DarkFraction.ToString("0.00", CultureInfo.InvariantCulture));
        sb.Append('\t').Append(companion.Torch.Reason);
        sb.Append('\t').Append(senses.Light.MeasuredSamples);
        sb.Append('\t').Append(senses.Light.ReadTick?.ToString(CultureInfo.InvariantCulture) ?? "-");
        sb.Append('\t').Append(light?.NominatedRegion is Infrastructure.Observation.LightSense.DarkRegion r
            ? FormattableString.Invariant($"{r.Centre.X},{r.Centre.Y}:{r.DarkSamples}") : "-");

        sb.Append('\t').Append(Tile(senses.Player.Bottom));
        sb.Append('\t').Append(senses.Player.Intent.X.ToString("0.0"));
        sb.Append('\t').Append(Main.LocalPlayer.dead ? 1 : 0);
        sb.Append('\t').Append(senses.Player.IsAttacking ? 1 : 0);
        sb.Append('\t').Append(senses.Player.IsChoppingTree ? 1 : 0);
        sb.Append('\t').Append(senses.Player.MinedOre != null ? 1 : 0);
        // What planning cost this tick, zero when nothing planned, and the last reach flood slice's wall-clock.
        sb.Append('\t').Append(brain.Navigator.TakePlanMs().ToString("0.00")).Append('\t').Append(brain.Positioner.LastFloodMs.ToString("0.00"));
        // The flood cost above is sticky (the last slice's, repeated until the next); these six
        // are this tick's, so a sum over a stretch of rows is the brain's real share of the wall.
        sb.Append('\t').Append(brain.SensesMs.ToString("0.00")).Append('\t').Append(brain.ReflexMs.ToString("0.00"))
          .Append('\t').Append(brain.DecideMs.ToString("0.00")).Append('\t').Append(brain.PositionMs.ToString("0.00"))
          .Append('\t').Append(brain.NavigateMs.ToString("0.00")).Append('\t').Append(brain.TotalMs.ToString("0.00"))
          .Append('\t').Append(Infrastructure.Movement.ClearanceField.Shared.Builds)
          // Ticks sealed off from the player; a roam is a wander row while this stays above zero.
          .Append('\t').Append(brain.StrandedTicks)
          .Append('\t').Append(brain.Positioner.ReachCount)
          .Append('\t').Append(brain.Positioner.ReturnableCount)
          // Whether the flood is settled, beside the two sizes. Without it a small region and an
          // unfinished one read the same, and "the tile is not in the set" means opposite things.
          .Append('\t').Append(brain.Positioner.ReachComplete ? 1 : 0)
          .Append('\t').Append(brain.Positioner.ChosenReturnable ? 1 : 0)
          .Append('\t').Append(brain.Positioner.PlayerOnlyOneWay ? 1 : 0);

        sb.Append('\t').Append(combat?.OfferedPlan?.Id ?? -1);
        sb.Append('\t').Append(combat?.OfferedSegment ?? -1);
        sb.Append('\t').Append(PlanStand(combat));
        sb.Append('\t').Append(mine?.JobId ?? 0);
        sb.Append('\t').Append(mine?.Policy.ToString() ?? "unavailable");
        sb.Append('\t').Append(mine?.Status ?? "unavailable");
        sb.Append('\t').Append(mine?.RemainingTiles ?? 0);
        sb.Append('\t').Append(mine?.TargetTile?.ToString() ?? "-");
        sb.Append('\t').Append(companion.Combat.Planner.LastInvalidation);
        sb.Append('\t').Append(FormattableString.Invariant($"{AttackLearning.LastSampledFactor:0.000},{AttackLearning.LastMeanFactor:0.000}"));
        sb.Append('\t').Append(ShotOutcomes.LastClosed is { } closed ? closed.Ratio.ToString("0.000", CultureInfo.InvariantCulture) : "-1");

        Player player = Main.LocalPlayer;
        sb.Append('\t').Append(sessionClock.Elapsed.TotalMilliseconds.ToString("0.000", CultureInfo.InvariantCulture));
        // When in the tick this row was sampled, named so a reader never has to infer it: inside the
        // AI phase, after the brain and the motor's own contact, before the engine's position += velocity.
        sb.Append("\tin_ai_after_contact_before_engine_move");
        sb.Append('\t').Append(Pair(player.Bottom));
        sb.Append('\t').Append(Pair(player.velocity));
        sb.Append('\t').Append(player.velocity.Y == 0f ? 1 : 0);
        sb.Append('\t').Append(player.shimmerWet ? "shimmer" : Liquid(player.wet, player.honeyWet, player.lavaWet));
        sb.Append('\t').Append(player.statLife).Append('/').Append(player.statLifeMax2);
        string playerHit = pendingPlayerHit ?? "-";
        pendingPlayerHit = null;
        sb.Append('\t').Append(playerHit);
        string companionHit = pendingCompanionHit ?? "-";
        pendingCompanionHit = null;
        sb.Append('\t').Append(companionHit);
        sb.Append('\t').Append(player.dead ? "dead" : "alive");
        sb.Append('\t').Append(PlayerActivity(player, senses));
        sb.Append('\t').Append(SupportAt(player.Bottom));
        sb.Append('\t').Append(DescribeControls(companion.Motor.AppliedControls));
        sb.Append('\t').Append(companion.Motor.ControlSource);
        sb.Append('\t').Append(brainExecuted ? 1 : 0);
        sb.Append('\t').Append(Pair(companion.Motor.DesiredVelocity));
        sb.Append('\t').Append(brain.Navigator.Status).Append('\t').Append(brain.Positioner.ChoiceReason);
        sb.Append('\t').Append(brain.MovementStalled ? 1 : 0);
        sb.Append('\t').Append(PlanVector(combat));
        sb.Append('\t').Append(combat?.OfferedPlan?.TargetKillTicks?.Length ?? 0)
            .Append('\t').Append((combat?.OfferedPlan?.Outcome.PlayerHarmPrevented ?? 0f).ToString("0.000", CultureInfo.InvariantCulture));
        sb.Append('\t').Append(companion.Combat.CooldownTicks).Append('\t').Append((int)(combat?.ForecastTicks() ?? 0)).Append('\t').Append(combat?.EligibilityReason ?? "unavailable");
        // `choice_id` and `choice_tick` come from the course since schema 0.43.0. They used to read
        // `Chooser.EvaluationId`, which the course brain never advances, so from the tick switch until
        // that bump every row and every `tool-effect` in a played session claimed comparison identity
        // zero — the join from a strike back to the decision that chose it was silently meaningless
        // rather than missing, which is the worse of the two.
        sb.Append('\t').Append(choiceEvaluated ? 1 : 0).Append('\t').Append(brain.Course.DecisionId)
            .Append('\t').Append(brain.Course.DecisionTick?.ToString(CultureInfo.InvariantCulture) ?? "-1");
        sb.Append('\t').Append(controlFresh ? 1 : 0).Append('\t').Append(controlGrant?.Id ?? 0)
            .Append('\t').Append(controlGrant?.Tick.ToString(CultureInfo.InvariantCulture) ?? "-1")
            .Append('\t').Append(controlGrant?.Hand.ToString() ?? "unavailable")
            .Append('\t').Append(controlGrant?.RequestedOwner ?? "unavailable")
            .Append('\t').Append(controlGrant?.MotorApplications ?? 0)
            .Append('\t').Append(controlFresh ? brain.FinaliseMs.ToString("0.00", CultureInfo.InvariantCulture) : "0.00");
        sb.Append('\t').Append(senses.Player.Intent.Y.ToString("0.000", CultureInfo.InvariantCulture))
            .Append('\t').Append(senses.Player.Activity.Confidence.ToString("0.000", CultureInfo.InvariantCulture))
            .Append('\t').Append(senses.Player.Activity.Samples)
            .Append('\t').Append(senses.Player.Activity.LocalWorkFraction.ToString("0.000", CultureInfo.InvariantCulture));
        sb.Append('\t').Append((mine?.RemainingWork?.Ticks ?? -1f).ToString("0.000", CultureInfo.InvariantCulture))
            .Append('\t').Append(mine?.RemainingWork?.Hits ?? -1)
            .Append('\t').Append((chop?.RemainingWork?.Ticks ?? -1f).ToString("0.000", CultureInfo.InvariantCulture))
            .Append('\t').Append(chop?.RemainingWork?.Hits ?? -1);
        sb.Append('\t').Append(brain.Companionship.Reunion.ApartTicks)
            .Append('\t').Append(brain.Companionship.Reunion.Departure.ToString("0.000", CultureInfo.InvariantCulture))
            .Append('\t').Append(brain.Companionship.Reunion.DelayCostPerTick.ToString("0.000000", CultureInfo.InvariantCulture));
        sb.Append('\t').Append(brain.LastAction is Activities.NearbyAssistance.CollectNearbyItems collection
            ? collection.Method : "none");
        var end = mine?.LastConclusion;
        sb.Append('\t').Append(end?.JobId ?? 0)
            .Append('\t').Append(end?.Tick.ToString(CultureInfo.InvariantCulture) ?? "-1")
            .Append('\t').Append(end?.Reason ?? "none")
            .Append('\t').Append(end?.Tracked ?? -1)
            .Append('\t').Append(end?.Present ?? -1)
            .Append('\t').Append(end?.Changed ?? -1)
            .Append('\t').Append(end?.Missing ?? -1)
            .Append('\t').Append(end?.Unobserved ?? -1)
            .Append('\t').Append(end?.CompanionRemovals ?? -1)
            .Append('\t').Append(end?.ObservedClear == true ? 1 : 0);
        var owner = brain.Activity;
        var attempt = owner.LastAttempt;
        sb.Append('\t').Append(owner.AttemptOpen ? owner.AttemptId : 0)
            .Append('\t').Append(attempt?.AttemptId ?? 0)
            .Append('\t').Append(attempt?.ActivityId ?? 0)
            .Append('\t').Append(attempt?.Activity ?? "none")
            .Append('\t').Append(attempt?.Family.ToString() ?? "none")
            .Append('\t').Append(attempt?.Status.ToString() ?? "none")
            .Append('\t').Append(attempt?.Cause ?? "none")
            .Append('\t').Append(attempt?.Attribution.ToString() ?? "none")
            .Append('\t').Append(attempt?.ProductiveEffects ?? -1)
            .Append('\t').Append(attempt?.StartTick.ToString(CultureInfo.InvariantCulture) ?? "-1")
            .Append('\t').Append(attempt?.EndTick.ToString(CultureInfo.InvariantCulture) ?? "-1");
        // The course's own admission, keyed like the raw/final pairs. Since schema 0.44.0 this reads the
        // three-valued census admission rather than the retired chooser's eligibility, for the reason
        // `CourseWorthOf` gives: the chooser's board has not been filled in a played session since
        // `0bb2c8a`, so every one of these columns read the literal `not-compared`, and
        // `CheckOffersAttemptsAndGrants` — whose whole subject is this column — was reading that.
        //
        // The vocabulary is deliberately the course's three values rather than a translation into the old
        // eligibility enum. A translation would have to invent which `OfferEligibility` a partly-usable
        // census corresponds to, and the distinction the course actually draws is the one the offer
        // vocabulary was reaching for anyway: usable, not yet known, proven unusable. `not-compared`
        // survives for an activity the course mints no domain for, which is its original meaning.
        foreach (var a in brain.Actions)
        {
            var worth = ReadCourseWorthPerActivity.Of(brain, a);
            string offer = worth.Offer == ReadCourseWorthPerActivity.NotCompared
                ? ReadCourseWorthPerActivity.NotCompared
                : worth.Offer + ":" + worth.OfferReason;
            sb.Append('\t').Append(offer);
        }
        // The meeting place keeps company's reunion reason and prices; -1 is an unpriced time, and a
        // reason of not-reuniting means the columns describe no current destination.
        var meeting = brain.Meeting;
        bool reuniting = meeting.Reason != "not-reuniting";
        sb.Append('\t').Append(meeting.Reason)
            .Append('\t').Append(reuniting ? FormattableString.Invariant($"{meeting.Anchor.X:0},{meeting.Anchor.Y:0}") : "-")
            .Append('\t').Append(float.IsNaN(meeting.PlayerTicks) ? "-1" : meeting.PlayerTicks.ToString("0.0", CultureInfo.InvariantCulture))
            .Append('\t').Append(float.IsNaN(meeting.CompanionTicks) ? "-1" : meeting.CompanionTicks.ToString("0.0", CultureInfo.InvariantCulture))
            .Append('\t').Append(meeting.Candidates.Count)
            .Append('\t').Append(meeting.Priced)
            .Append('\t').Append(meeting.FloodState);
        // Why the held movement goal is not being delivered, sticky until delivery or a new goal,
        // joined to the search and attempt it came from; and how attempts have ended, with physical
        // completion and voluntary cancellation counted apart.
        // The orb's navigator names the reason in its progress reason and the status; the walker's failure classes are gone.
        bool undelivered = brain.Navigator.Status is Infrastructure.Movement.Navigator.ExecutionStatus.Unreachable;
        sb.Append('\t').Append(undelivered ? "Unreachable" : "None")
            .Append('\t').Append(undelivered ? brain.Navigator.ProgressReason : "-")
            .Append('\t').Append(undelivered ? brain.Navigator.SearchId : 0)
            .Append('\t').Append(undelivered ? brain.Navigator.AttemptId : 0)
            .Append('\t').Append(brain.Navigator.LastEnding?.ToString() ?? "-")
            .Append('\t').Append(brain.Navigator.CompletedAttempts)
            .Append('\t').Append(brain.Navigator.FailedAttempts)
            .Append('\t').Append(brain.Navigator.PreemptedAttempts)
            .Append('\t').Append(brain.Navigator.CancelledAttempts);
        // Where the feet are going, what the hands chose and what the game says a companion shot struck
        // are three different facts, so each has its own slot:generation column; a shot at the visible
        // enemy is not progress against the hidden one a hunt walks toward, and a piercing arrow aimed at
        // one enemy can land on the one in front. Guard's removal estimate and share, and what the most
        // urgent threat's hit costs each body after defence, sit beside them. Infinity is written as -1
        // so every numeric column parses as a number.
        static string Identity(NPC? subject) => subject != null && subject.active
            ? string.Create(CultureInfo.InvariantCulture, $"{subject.whoAmI}:{Infrastructure.Observation.HostileAttackSources.Generation(subject)}") : "-";
        sb.Append('\t').Append(PlanTargets(combat));
        sb.Append('\t').Append((combat?.OfferedPlan?.Outcome.DamagePerSecond ?? 0f).ToString("0.000", CultureInfo.InvariantCulture));
        sb.Append('\t').Append(PlanTravel(combat).ToString("0.0", CultureInfo.InvariantCulture));
        sb.Append('\t').Append(PlanUses(combat));
        sb.Append('\t').Append(Identity(brain.EngageTarget));
        var landed = TrackLandedHits.Last;
        sb.Append('\t').Append(landed is { } hit ? string.Create(CultureInfo.InvariantCulture, $"{hit.HitSlot}:{hit.HitGeneration}") : "-");
        sb.Append('\t').Append(landed is { } aimed ? string.Create(CultureInfo.InvariantCulture, $"{aimed.AimSlot}:{aimed.AimGeneration}") : "-");
        sb.Append('\t').Append(landed?.Damage ?? 0);
        sb.Append('\t').Append(landed?.Tick.ToString(CultureInfo.InvariantCulture) ?? "-1");
        sb.Append('\t').Append(TrackLandedHits.Count);
        sb.Append('\t').Append(PlanKillTick(combat, senses.Tick));
        sb.Append('\t').Append((combat?.OfferedPlan?.Outcome.ThreatRemoved ?? 0f).ToString("0.000", CultureInfo.InvariantCulture));
        sb.Append('\t').Append((top?.EffectiveDamageToPlayer ?? 0f).ToString("0.0", CultureInfo.InvariantCulture));
        sb.Append('\t').Append((top?.EffectiveDamageToCompanion ?? 0f).ToString("0.0", CultureInfo.InvariantCulture));
        var encounter = brain.Senses.Encounter;
        sb.Append('\t').Append(encounter.Intensity.ToString("0.000", CultureInfo.InvariantCulture));
        sb.Append('\t').Append(encounter.Source);
        sb.Append('\t').Append(encounter.Recognised ? 1 : 0);
        sb.Append('\t').Append(encounter.PressureTicks);
        sb.Append('\t').Append((combat?.OfferedPlan?.Outcome.TimeToFirstDamage ?? 1f).ToString("0.000", CultureInfo.InvariantCulture));
        var region = brain.Positioner.Region;
        // A claimed arrival is the navigator reporting Arrived on a tick the ordinary branch asked it to travel;
        // any other owner leaves the status from an earlier MoveTo, which is not a claim about this tick.
        // The body judged is the orb's centre, the one point the navigator and the region both measure.
        bool arrivalClaimed = brain.Navigator.Status == Infrastructure.Movement.Navigator.ExecutionStatus.Arrived
            && controlGrant?.RequestedOwner == "travel";
        bool? inside = arrivalClaimed ? region.Contains(npc.Center) : null;
        sb.Append('\t').Append(region.Name)
            .Append('\t').Append(brain.Positioner.ChosenRevision)
            .Append('\t').Append(region.AdmittedTick)
            .Append('\t').Append(region.TerrainRevision)
            .Append('\t').Append(region.Kind == Infrastructure.Position.SuccessRegionKind.None ? "-" : Pair(region.Anchor))
            .Append('\t').Append(region.Kind == Infrastructure.Position.SuccessRegionKind.FollowComfort ? Pair(region.PlayerFeet) : "-")
            .Append('\t').Append(region.Kind == Infrastructure.Position.SuccessRegionKind.FollowComfort ? Pair(region.Comfort) : "-")
            .Append('\t').Append(region.WorkTile is Point work ? $"{work.X},{work.Y}" : "-")
            .Append('\t').Append(region.Kind == Infrastructure.Position.SuccessRegionKind.ToolReach ? $"{region.ReachX},{region.ReachY}" : "-")
            .Append('\t').Append(!arrivalClaimed ? "-" : inside is bool b ? (b ? "inside" : "outside") : "undeclared");
        sb.Append('\t').Append(double.IsNaN(lastRecordMs) ? "-" : lastRecordMs.ToString("0.000", CultureInfo.InvariantCulture))
            .Append('\t').Append(GodsEyeEvents.Written)
            .Append('\t').Append(GodsEyeEvents.Dropped)
            .Append('\t').Append(GodsEyeEvents.Coalesced)
            .Append('\t').Append(RecordTerrainChunks.Evictions);
        sb.Append('\t').Append(TravelEpisodes.StopsPerMinute.ToString("0.00", CultureInfo.InvariantCulture))
            .Append('\t').Append(TravelEpisodes.RouteSpeedMean.ToString("0.00", CultureInfo.InvariantCulture));
        var lighting = brain.Actions.OfType<Activities.NearbyAssistance.LightUsefulArea>().FirstOrDefault();
        sb.Append('\t').Append(string.IsNullOrEmpty(lighting?.LastSearchSites) ? "-" : lighting!.LastSearchSites)
            .Append('\t').Append(lighting?.LastSearchAsked ?? 0);
        sb.Append('\t').Append(Movement.TerrainChanges.Revision);
        var intent = senses.Intent.Region;
        sb.Append('\t').Append(FormattableString.Invariant(
                $"{intent.Centre.X:0},{intent.Centre.Y:0};{intent.HalfSize.X:0},{intent.HalfSize.Y:0}"))
            .Append('\t').Append(intent.Pull(companion.NPC.Bottom).ToString("0.000", CultureInfo.InvariantCulture));
        // Schema 0.45.0: these two read the published course. They read `Chooser.LastTaskOrder` and
        // `Chooser.LastTaskOrderRunnerUp` until now, which is `OrderNearbyTasks`' permutation scoring —
        // retired with the family chooser on `0bb2c8a` — so **every row of every capture since that
        // commit wrote a dash in both**, which is this folder's own trap wearing its plainest face: a
        // frozen empty string is a legal string, nothing went red, and a reader asking a capture "what
        // order did it consider" got nothing at all. The name and the position do not move; the meaning
        // does, which is the one change the append convention cannot carry and is what the schema bump
        // is for.
        //
        // `task_order` is the published course's bound steps, in the order the course will perform
        // them, by purpose. A course with no steps is companionship and writes `-`, as it did before
        // for fewer than two close jobs.
        //
        // `task_order_runner_up` is the best priced order whose first step differs from the published
        // one's — `DecideCourseEachTick.LastRunnerUpOrder`, retained by the search since 5c39e91 — as
        // `purpose>purpose@value`, and `-` when the search priced fewer than two distinct first steps.
        // That dash is a fact about this decision's search rather than about the recorder having nothing
        // to ask, which is why the column wrote the reason it could not be filled until the accessor
        // existed: a dash there was exactly what the dead column had written since 0bb2c8a.
        var published = brain.Course.Course.Current?.Projection.Steps;
        var runnerUp = brain.Course.LastRunnerUpOrder;
        sb.Append('\t').Append(published == null || published.Count == 0
                ? "-"
                : string.Join(">", published.Select(step => step.Opportunity.Purpose)))
            .Append('\t').Append(runnerUp is not { } second || second.Purposes.Count == 0
                ? "-"
                : string.Join(">", second.Purposes) + "@" + second.Value.ToString("0.000", CultureInfo.InvariantCulture));
        // The player's own smart cursor as the reference lighting is judged against: the tile it would offer him, that
        // tile's own light, what that light says to placing a torch, and the stage at which lighting refuses the tile.
        var referenceReading = lighting?.PlayerReferenceReading;
        sb.Append('\t').Append(lighting?.PlayerReferenceTile is Point referenceTile ? FormattableString.Invariant($"{referenceTile.X},{referenceTile.Y}") : "-")
            .Append('\t').Append(referenceReading is { Light: not Observation.LightSense.PlacementLight.Unread } read
                ? read.Brightness.ToString("0.000", CultureInfo.InvariantCulture) : "-")
            .Append('\t').Append(referenceReading?.Light switch
            {
                Observation.LightSense.PlacementLight.Dark => "dark",
                Observation.LightSense.PlacementLight.Lit => "lit",
                Observation.LightSense.PlacementLight.Sky => "sky",
                Observation.LightSense.PlacementLight.Unread => "unread",
                _ => "-",
            })
            .Append('\t').Append(lighting?.PlayerReferenceStage ?? "-");
        // This tick's garbage collections per generation, so a hitch in the brain's share can be told from a collection.
        int gc0 = GC.CollectionCount(0), gc1 = GC.CollectionCount(1), gc2 = GC.CollectionCount(2);
        sb.Append('\t').Append(lastGc0 < 0 ? 0 : gc0 - lastGc0)
            .Append('\t').Append(lastGc1 < 0 ? 0 : gc1 - lastGc1)
            .Append('\t').Append(lastGc2 < 0 ? 0 : gc2 - lastGc2);
        lastGc0 = gc0; lastGc1 = gc1; lastGc2 = gc2;

        // Lane C: the evade layer's verdict, matching the header block of the same name.
        var evade = brain.Movement.LastEvade;
        sb.Append('\t').Append(evade.Reason switch { EvadeReason.Kept => "kept", EvadeReason.Hit => "hit", EvadeReason.Spent => "spent", _ => "off" })
            .Append('\t').Append(evade.HitTick)
            .Append('\t').Append(!evade.Bent ? "-" : evade.Choice switch { EvadeChoice.Stop => "stop", EvadeChoice.JobHeading => "job-heading", _ => "heading" })
            .Append('\t').Append(evade.RefusedNowhere)
            .Append('\t').Append(evade.RefusedDanger);

        // Schema 0.45.0: the whole update, and the parts of it this mod can account for. Written last in
        // the row and read in the same order the header declares them.
        double frameMs = FrameCost.IntervalMilliseconds;
        double remainder = FrameCost.RemainderMilliseconds(lastBrainMs, lastRecordMs);
        sb.Append('\t').Append(frameMs < 0 ? "-1.00" : frameMs.ToString("0.00", CultureInfo.InvariantCulture))
            .Append('\t').Append(FrameCost.Draws)
            .Append('\t').Append(FrameCost.OverlayMilliseconds.ToString("0.00", CultureInfo.InvariantCulture))
            .Append('\t').Append(FrameCost.InspectorMilliseconds.ToString("0.00", CultureInfo.InvariantCulture))
            .Append('\t').Append(remainder < 0 && frameMs < 0 ? "-1.00" : remainder.ToString("0.00", CultureInfo.InvariantCulture));
        ObserveFrameOverrun(frameMs, remainder, npc);
        lastBrainMs = brain.TotalMs;
        // What the binder read about the targets its own census admitted, held by the audit from the
        // moment the decision recorded itself, because the frozen observation is in hand there and not
        // here. A dash is a decision that contradicted nothing, never an absence of evidence.
        sb.Append('\t').Append(AuditDecisionContracts.LastTargetEvidence.Length == 0 ? "-" : AuditDecisionContracts.LastTargetEvidence)
            .Append('\t').Append(AuditDecisionContracts.LastTargetEvidenceAge.Length == 0 ? "-" : AuditDecisionContracts.LastTargetEvidenceAge);
        sb.Append('\t').Append(SupportBelow(npc.Bottom));
        AppendCostColumns(sb, brain, brainExecuted);
        row.Dispose();

        // A write that fails (disk full, a stream the OS closed) must not escape the NPC's AI
        // and take the companion with it; the record stops and the game goes on.
        var enqueueing = BrainSections.Enter(RecordEnqueueSection);
        try
        {
            QueueDiagnosticRecords.TryEnqueueTsv(sb.ToString());
            rowsWritten++;
            if (++sinceFlush >= FlushEveryTicks)
            {
                sinceFlush = 0;
                GodsEyeEvents.Flush();
            }
        }
        catch (Exception e)
        {
            ModContent.GetInstance<AICompanion>().Logger.Error($"BrainTelemetry: write failed, recording stops: {e.Message}");
            diagnosticWriter?.Dispose(); diagnosticWriter = null;
        }
        enqueueing.Dispose();
        recording.Dispose();
        // Raw clock ticks rather than `Stopwatch.Elapsed`, which truncates to 100 ns, so the section closed on the
        // line above is inside this figure exactly.
        lastRecordMs = (Stopwatch.GetTimestamp() - recordStarted) * BrainSections.MillisecondsPerTimestamp;
    }

    private static string PlanWeaponName(Activities.Combat.FightEnemies? combat, CompanionNPC companion)
    {
        var plan = combat?.OfferedPlan;
        if (plan == null)
            return "-";
        var weapons = companion.Combat.Weapons;
        foreach (var segment in plan.Segments)
            foreach (var use in segment.Uses)
                if ((uint)use.WeaponSlot < (uint)weapons.Count)
                    return weapons[use.WeaponSlot].Name;
        return "-";
    }

    private static string PlanStand(Activities.Combat.FightEnemies? combat)
    {
        var plan = combat?.OfferedPlan;
        if (plan == null || combat!.OfferedSegment < 0 || combat.OfferedSegment >= plan.Segments.Length)
            return "-";
        Vector2 stand = plan.Segments[combat.OfferedSegment].Stand.Stand;
        return FormattableString.Invariant($"{stand.X:0},{stand.Y:0}");
    }

    private static string PlanVector(Activities.Combat.FightEnemies? combat)
    {
        var plan = combat?.OfferedPlan;
        if (plan == null)
            return "-";
        var outcome = plan.Outcome;
        return FormattableString.Invariant($"{outcome.DamagePerSecond:0.000},{outcome.ThreatRemoved:0.000},{outcome.PlayerHarmPrevented:0.000},{outcome.CompanionHarmTaken:0.000},{outcome.PushDangerAdded:0.000},{outcome.CompanyGap:0.000},{outcome.TimeToFirstDamage:0.000},{outcome.ManaSpent:0.000}");
    }

    private static string PlanTargets(Activities.Combat.FightEnemies? combat)
    {
        var targets = combat?.OfferedPlan?.Validity.Targets;
        if (targets == null || targets.Length == 0)
            return "-";
        var sb = new StringBuilder();
        foreach ((int slot, int generation) in targets)
        {
            if (sb.Length > 0) sb.Append('|');
            sb.Append(FormattableString.Invariant($"{slot}:{generation}"));
        }
        return sb.ToString();
    }

    private static string PlanUses(Activities.Combat.FightEnemies? combat)
    {
        var plan = combat?.OfferedPlan;
        if (plan == null || combat!.OfferedSegment < 0 || combat.OfferedSegment >= plan.Segments.Length)
            return "-";
        var uses = plan.Segments[combat.OfferedSegment].Uses;
        if (uses.Length == 0)
            return "-";
        var sb = new StringBuilder();
        foreach (var use in uses)
        {
            if (sb.Length > 0) sb.Append('|');
            sb.Append(FormattableString.Invariant($"{use.WeaponSlot}>{use.TargetSlot}@{use.FireTick}"));
        }
        return sb.ToString();
    }

    private static float PlanTravel(Activities.Combat.FightEnemies? combat)
    {
        var plan = combat?.OfferedPlan;
        if (plan == null || combat!.OfferedSegment < 0 || combat.OfferedSegment >= plan.Segments.Length)
            return 0f;
        return plan.Segments[combat.OfferedSegment].Verdict.TravelTicks;
    }

    private static string PlanKillTick(Activities.Combat.FightEnemies? combat, int tick)
    {
        var plan = combat?.OfferedPlan;
        var kills = plan?.TargetKillTicks;
        if (plan == null || kills == null)
            return "-1";
        foreach ((int slot, int at) in kills)
            if (slot == plan.PrimaryTarget)
                return Math.Max(0, at - tick).ToString(CultureInfo.InvariantCulture);
        return "-1";
    }

    private static string Tile(Vector2 world) => $"{(int)(world.X / 16f)},{(int)(world.Y / 16f)}";

    private static string Pair(Vector2 value) => $"{value.X.ToString("0.00", CultureInfo.InvariantCulture)},{value.Y.ToString("0.00", CultureInfo.InvariantCulture)}";

    private static string Liquid(bool wet, bool honey, bool lava) => lava ? "lava" : honey ? "honey" : wet ? "water" : "dry";

    private static string PlayerActivity(Player player, Infrastructure.Observation.Senses senses)
        => senses.Player.IsChoppingTree ? "chop"
         : senses.Player.MinedOre != null ? "mine"
         : senses.Player.IsAttacking ? "attack"
         : player.itemAnimation > 0 ? "item-use"
         : MathF.Abs(player.velocity.X) > 0.2f ? "move"
         : "idle";

    private static string SupportAt(Vector2 bottom)
    {
        // FeetTile is the final pixel occupied by the body, above a flat support. The support
        // probe starts at the first pixel below the body so ordinary ground does not read air.
        Point feet = new((int)MathF.Floor(bottom.X / 16f), (int)MathF.Floor(bottom.Y / 16f));
        TileShape shape = MovementQueries.World.Shape(feet.X, feet.Y);
        bool passThrough = MovementQueries.World.PassThrough(feet.X, feet.Y);
        return shape switch
        {
            // A hammered platform retains its fall-through rule even when its shape is a slope;
            // exposing that distinction lets the reader recognise a stair tread sequence without
            // calling every slope a stair from position alone.
            TileShape.SolidLowerLeft => passThrough ? "platform-slope-lower-left" : "slope-lower-left",
            TileShape.SolidLowerRight => passThrough ? "platform-slope-lower-right" : "slope-lower-right",
            TileShape.SolidUpperLeft => "slope-upper-left",
            TileShape.SolidUpperRight => "slope-upper-right",
            TileShape.Half => "half-block",
            // No `solid-platform` arm: `ReadGameTerrain.Shape` returns `Platform` only where
            // `PassThrough` is already true, and `TextTileWorld` maps the same glyphs both ways, so a
            // non-pass-through platform is unreachable by construction in every world this reads. The
            // arm existed and wrote a string no capture could ever carry, which is a reader being
            // taught to look for a state that does not exist.
            TileShape.Platform => "platform",
            TileShape.Solid => "solid",
            _ => "air",
        };
    }

    /// <summary>
    /// How far the body's bottom edge is above the first support beneath it, in pixels, counting a
    /// platform as support — <see cref="MovementQueries.IsSupport"/> is the predicate, so this is the
    /// same "is there something here" every search asks rather than a second opinion about tiles.
    ///
    /// <b>A negative value is the reading this exists for.</b> A body resting on a surface reads zero
    /// or a little above; a body whose bottom edge is below the top of the tile supporting it is
    /// *inside* that tile, and the number says by how much. Before this column a body sinking into a
    /// platform was a reconstruction from the body's centre, the tile grid and a radius, which is the
    /// arithmetic a reader gets wrong by a body radius when `npc_px` changed from feet to centre.
    ///
    /// A dash means no support was found inside the window, which is not the same fact as no support:
    /// the probe stops at <see cref="SupportProbeTiles"/> tiles, because an orb over a chasm would
    /// otherwise walk the column to the world's floor on every tick of a long fall.
    /// </summary>
    /// <summary>Identities the loot sense has already been seen admitting, so a drop lying on the floor
    /// for a thousand ticks is one occurrence and not a thousand.</summary>
    private static readonly HashSet<int> sightedDrops = new();

    /// <summary>
    /// One <c>drop-sighted</c> occurrence per drop, on the tick the loot sense first admits it.
    ///
    /// <b>What a capture could not say before it.</b> An item reached the record only by being
    /// considered in a candidate funnel or by being picked up, so a drop the companion saw and never
    /// went for existed nowhere — three of the 22 September capture's tail drops are nameable in no
    /// record at all — and a world run had nothing to stage. The sighting is the sense's own admission
    /// rather than a decision about the drop, so it fires whatever the course then does.
    ///
    /// It reads <c>Senses.Loot</c> from here rather than from Observation, because the recorder is the
    /// consumer and a sense does not write occurrences; the identity is the god's-eye item identity, so
    /// a sighting and a later pickup join on the same number.
    /// </summary>
    private static void WatchSightedDrops(Infrastructure.Observation.Senses senses)
    {
        if (!GodsEyeEvents.Active) return;
        // A slot emptied and refilled gives a new identity rather than a new slot, so the set is
        // pruned by what is live this tick rather than by a bound: the sense holds only drops inside
        // its own search radius, so it cannot grow without limit.
        if (sightedDrops.Count > 4096) sightedDrops.Clear();
        foreach (Observation.LootSense.Pickup drop in senses.Loot.Pickups)
        {
            if (!Observation.LootSense.IsWorldDrop(drop.Item)) continue;
            if (!sightedDrops.Add(GodsEyeEvents.ItemIdentity(drop.Item))) continue;
            GodsEyeEvents.RecordDropSighted(drop.Item, drop.DistanceToCompanion, drop.Value);
        }
    }

    private static string SupportBelow(Vector2 bottom)
    {
        int x = (int)MathF.Floor(bottom.X / 16f);
        int feet = (int)MathF.Floor(bottom.Y / 16f);
        for (int y = feet; y < feet + SupportProbeTiles; y++)
        {
            if (!MovementQueries.IsSupport(x, y)) continue;
            return (y * 16f - bottom.Y).ToString("0.00", CultureInfo.InvariantCulture);
        }
        return "-";
    }

    /// <summary>How far down <see cref="SupportBelow"/> looks. Two screens at the game's own tile size,
    /// which is past anything a hover holds and short of walking a shaft to the world's floor.</summary>
    private const int SupportProbeTiles = 64;

    internal static string DescribeControls(Controls controls)
        => $"desired={controls.Desired.X.ToString("0.00", CultureInfo.InvariantCulture)},{controls.Desired.Y.ToString("0.00", CultureInfo.InvariantCulture)}";

    /// <summary>The motor's liquid kind by name, in the game's own numbering: water, lava, honey, shimmer; -1 is dry.</summary>
    private static string LiquidName(int kind) => kind switch { 0 => "water", 1 => "lava", 2 => "honey", 3 => "shimmer", _ => "dry" };
}
