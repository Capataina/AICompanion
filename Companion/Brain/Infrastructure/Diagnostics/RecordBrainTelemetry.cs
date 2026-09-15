#nullable enable

using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;
using AICompanion.Companion.Brain.Infrastructure.Movement;
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
    private const string Schema = "0.36.0";

    /// <summary>
    /// One activity's factors from one comparison, as <c>name:value</c> pairs joined by commas: every multiplier its final
    /// carries, in the order <see cref="Selection.EvaluatePreparedActivities"/> and <see cref="Selection.OrderNearbyTasks"/>
    /// apply them, then the raw value and the final, then the error, the offer and the method evidence. The method evidence
    /// is last because its text is free and may itself hold commas; readers take a factor by its name, never by position.
    /// </summary>
    public static string FactorList(in Selection.Chooser.Scored score)
        => FormattableString.Invariant(
            $"protection:{score.Protection:0.000},commitment:{score.Commitment:0.000},horizon:{score.Horizon:0.000},useful-work:{score.UsefulWork:0.000},reunion:{score.Reunion:0.000},time:{score.Time:0.000},player-fit:{score.PlayerFit:0.000},order:{score.Order:0.000},raw:{score.Raw:0.000},final:{score.Final:0.000},")
            + $"error:{score.Error},offer:{score.Eligibility}/{score.EligibilityReason},method:{score.MethodEvidence}";

    // The garbage collector's per-generation counts at the last row, so each row carries its own tick's collections; -1 until
    // a session's first row has read them.
    private static int lastGc0 = -1, lastGc1 = -1, lastGc2 = -1;
    // The cost of the previous row's Record call: a row cannot contain the time spent writing itself, so each row carries
    // the one before it and the first row of a session carries none.
    private static readonly Stopwatch recordClock = new();
    private static double lastRecordMs = double.NaN;
    /// <summary>What the last Record call cost in milliseconds, NaN before the first; MeasureBrainCost samples it as its own phase.</summary>
    public static double LastRecordMilliseconds => lastRecordMs;
    // Rows this session has written, which the end marker states so a reader can tell a file that lost rows from one
    // that never had them.
    private static int rowsWritten;
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
    private readonly record struct RecordedConfiguration(Activities.WorkPolicy Mining, Activities.WorkPolicy Chopping, bool Hunting, bool PotBreaking,
        bool TorchPlacement, PlayerIntegration.CompanionDistanceMode DistanceMode, bool Inspector, bool RecordTelemetry)
    {
        public static RecordedConfiguration Current()
        {
            var preferences = PlayerIntegration.CompanionPreferences.Current;
            var switches = DiagnosticsConfiguration.CompanionDiagnosticsConfig.Current;
            return new(preferences.Mining, preferences.Chopping, preferences.Hunting, preferences.PotBreaking, preferences.TorchPlacement,
                preferences.DistanceMode, switches.EnableBrainInspector, switches.RecordTelemetry);
        }

        public string Describe()
            => $"character;mining={Mining};chopping={Chopping};hunting={Flag(Hunting)};pot_breaking={Flag(PotBreaking)};torch_placement={Flag(TorchPlacement)};distance_mode={DistanceMode};inspector={Flag(Inspector)};record_telemetry={Flag(RecordTelemetry)}";

        private static string Flag(bool value) => value ? "true" : "false";
    }
    private static string? pendingPlayerHit;
    private static string? pendingCompanionHit;
    private static string? lastDecision;
    private static bool firstUpdateRecorded;

    /// <summary>The folder the files land in: the mod's source folder, which is where the repository is.</summary>
    public static string Folder => Path.Combine(Main.SavePath, "ModSources", "AICompanion", "Telemetry");
    internal static double ElapsedMilliseconds => sessionClock.Elapsed.TotalMilliseconds;

    public override void OnWorldLoad()
    {
        Close("superseded-by-world-load");
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
            GodsEyeEvents.Open(eventsPath);
            WriteMetadata();
            GodsEyeEvents.RecordLifecycle("world-entry", "observed=ModSystem.OnWorldLoad;tag-load=not-yet-observed;outer-load=unobservable");
            pendingPlayerHit = null;
            pendingCompanionHit = null;
            lastDecision = null;
            firstUpdateRecorded = false;
            ScenarioCapture.Reset();
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
            if (writer != null) Close("recording-disabled");
            return;
        }
        if (writer == null || firstUpdateRecorded)
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
        // The census and the map are written here rather than per tick because both are one
        // artefact about the whole session; there is nothing to say until it is over. They land
        // beside the .tsv under the same stamp, so the session reader finds them without being
        // told where to look.
        WriteWhole(censusPath, BehaviourCensus.Report, "census");
        WriteWhole(mapPath, SessionMap.Report, "map");
        // The open journey and the open stop belong to this session, so they are written before the stream closes; the
        // census closes its own open episode inside Report for the same reason.
        TravelEpisodes.Close(CompanionNPC.Instance);
        GodsEyeEvents.Close();
        try
        {
            writer?.WriteLine($"# end={reason};rows={rowsWritten};events-written={GodsEyeEvents.Written};events-dropped={GodsEyeEvents.Dropped};events-coalesced={GodsEyeEvents.Coalesced};terrain-evictions={RecordTerrainChunks.Evictions}");
            writer?.Flush();
            writer?.Dispose();
        }
        catch (Exception e)
        {
            ModContent.GetInstance<AICompanion>().Logger.Warn($"BrainTelemetry: close failed: {e.Message}");
        }
        finally
        {
            sessionClock.Reset();
            writer = null;
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

    private static void WriteMetadata()
    {
        if (writer == null)
            return;
        writer.WriteLine($"# schema={Schema}");
        writer.WriteLine($"# started_utc={sessionStartedUtc:O}");
        writer.WriteLine($"# terraria={Main.versionNumber};tml_assembly={typeof(Main).Assembly.GetName().Version};runtime={Environment.Version};os={Environment.OSVersion.Platform}");
        writer.WriteLine("# mods=" + string.Join(";", (ModLoader.Mods ?? Array.Empty<Mod>()).Select(mod => mod.Name + "@" + mod.Version)));
        writer.WriteLine($"# source_revision={SourceProvenance}");
        writer.WriteLine($"# capabilities={DescribeCapabilities()}");
        writer.WriteLine($"# world={DescribeWorld()}");
        recordedConfiguration = RecordedConfiguration.Current();
        writer.WriteLine($"# config={recordedConfiguration.Describe()}");
        // What a capture keeps and what it forgets, read from the constants that bound each store, so a reader can tell an
        // absence the recorder never kept from one that did not happen without knowing the code.
        writer.WriteLine("# retention=rows=one-per-companion-ai-tick;events=every-occurrence-offered"
            + $";terrain-snapshots-remembered={RecordTerrainChunks.MaximumRemembered};terrain-captures-per-tick={RecordTerrainChunks.CapturesPerTick}"
            + $";recent-attempt-outcomes={Infrastructure.Selection.OwnCurrentActivity.RecentAttemptCapacity};cargo-transfer-ledger={(global::AICompanion.Companion.Inventory.CompanionInventory.RecentTransferCapacity)}"
            + $";cosmetic-contacts-per-summary={GodsEyeEvents.CosmeticContactsPerSummary};inspector-traces={BrainInspectorSamples.Capacity};inspector-cost-ticks={BrainInspectorSamples.CostTicks};session-map-tiles={SessionMap.MaxTilesRemembered}"
            + $";plan-dump-every-ticks={DumpEveryTicks};flush-every-ticks={FlushEveryTicks}");
        writer.WriteLine("# lifecycle=world-entry-observed;tag-load-not-yet-observed;first-update-not-yet-observed;outer-load-unobservable;save-not-observed");
        writer.Flush();
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
        if (path == null || writer == null)
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
        if (!DiagnosticsConfiguration.CompanionDiagnosticsConfig.Current.RecordTelemetry) { if (writer != null) Close("recording-disabled"); return; }
        if (writer == null)
            return;
        recordClock.Restart();
        var configuration = RecordedConfiguration.Current();
        if (configuration != recordedConfiguration)
        {
            recordedConfiguration = configuration;
            GodsEyeEvents.RecordConfiguration(configuration.Describe());
        }
        ScenarioCapture.Watch(companion);
        TravelEpisodes.Watch(companion);
        Brain brain = companion.Brain;
        var senses = brain.Senses;
        NPC npc = companion.NPC;
        RecordTerrainChunks.ObserveActors(npc, Main.LocalPlayer);
        string decision = brain.Reflexes.Active ?? brain.LastAction?.Name ?? "-";
        bool brainExecuted = brain.LastTick == Main.GameUpdateCount;
        bool choiceEvaluated = brainExecuted && brain.ChoiceEvaluated;
        var activity = brain.Chooser.Activity;
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
        var guard = default(Activities.Combat.ProtectPlayer);
        var mine = default(Activities.Gathering.MineOre);
        var chop = default(Activities.Gathering.ChopTree);
        var hunt = default(Activities.Combat.PursueAttackOpportunity);
        var light = default(Activities.NearbyAssistance.LightUsefulArea);
        foreach (var candidate in brain.Chooser.Actions)
        {
            if (candidate is Activities.NearbyAssistance.LightUsefulArea lightAction) light = lightAction;
            if (candidate is Activities.Combat.ProtectPlayer guardAction) guard = guardAction;
            if (candidate is Activities.Gathering.MineOre mineAction) mine = mineAction;
            if (candidate is Activities.Gathering.ChopTree chopAction) chop = chopAction;
            if (candidate is Activities.Combat.PursueAttackOpportunity huntAction) hunt = huntAction;
        }
        activityControls += $";mine-last-conclusion={mine?.LastConclusion?.ToString() ?? "none"}";
        if (decision != lastDecision || Main.GameUpdateCount % 60 == 0)
        {
            var board = new StringBuilder();
            foreach (var score in brain.Chooser.LastScores) { if (board.Length > 0) board.Append(','); board.Append(score.Action.Name).Append('=').Append(score.Raw.ToString("0.000", CultureInfo.InvariantCulture)).Append("->").Append(score.Final.ToString("0.000", CultureInfo.InvariantCulture)); }
            board.Append(CultureInfo.InvariantCulture, $";regroup={brain.Chooser.RegroupUrgency:0.000};return-ticks={brain.Chooser.EstimatedReturnTicks:0.0}");
            foreach (var score in brain.Chooser.LastScores)
                board.Append(";factors:").Append(score.Action.Name).Append('=').Append(FactorList(score));
            foreach (var nomination in brain.Chooser.LastNominations)
                board.Append(CultureInfo.InvariantCulture, $";family:{nomination.Family}=child:{nomination.Activity?.Name ?? "none"},value:{nomination.Activity?.Final ?? 0:0.000}");
            foreach (var family in brain.Chooser.Queries.LastFamilies)
                board.Append(CultureInfo.InvariantCulture, $";queries:{family.Family}=prepared:{family.Prepared},deferred:{family.Deferred},ms:{family.Milliseconds:0.000}");
            var preferences = PlayerIntegration.CompanionPreferences.Current;
            board.Append(CultureInfo.InvariantCulture, $";movement-stalled={brain.MovementStalled};activity-status={brain.ActivityStatus};activity-target={brain.LastAction?.ActivityTarget};activity-radius={preferences.NewActivityRadius};continuation-radius={preferences.ActiveActivityRadius};recovery-radius={preferences.RecoveryRadius}");
            GodsEyeEvents.RecordDecision(npc, brain.LastAction?.Name ?? "-", board.ToString(), brain.LastRequest.Kind.ToString(),
                activityControls + $";freshness={(choiceEvaluated ? "fresh" : "stale-or-not-executed")};brain-fresh={brainExecuted};choice-id={brain.Chooser.EvaluationId};choice-tick={brain.Chooser.EvaluationTick?.ToString(CultureInfo.InvariantCulture) ?? "unavailable"};execution={decision};control-source={companion.Motor.ControlSource}");
            lastDecision = decision;
        }
        GodsEyeEvents.RecordMovementState(npc, brain.Navigator);
        GodsEyeEvents.RecordNavigationEvidence(npc, brainExecuted, decision, brain.LastRequest.Kind.ToString(), activityControls,
            brain.Navigator.SearchId, brain.Navigator.AttemptId, brain.Navigator.SearchExpansions, brain.Navigator.SearchPending,
            brain.Navigator.ProgressReason, 0,
            brain.Positioner.CandidateCount, brain.Positioner.ReachableCandidateCount, brain.Positioner.RejectedCandidateCount, brain.Positioner.ChoiceReason,
            senses.Threats.InterventionTicks, senses.Threats.ProtectionUrgency,
            senses.Threats.MostUrgent?.PredictionConfidence ?? 0f, senses.Threats.MostUrgent?.PredictionSamples ?? 0,
            $"route-completed-steps={brain.Navigator.Path?.Index ?? 0};route-remaining-estimated-ticks={brain.Navigator.RemainingEstimatedRouteTicks:0.000};follow-objective-valid={brain.Positioner.FollowObjectiveSatisfied};follow-horizontal-gap={brain.Positioner.FollowHorizontalGap:0.000};follow-vertical-gap={brain.Positioner.FollowVerticalGap:0.000};follow-objective={brain.Positioner.FollowObjectiveReason};recovery-active={brain.FollowRecovery.Active};recovery-reason={brain.FollowRecovery.Reason};recovery-flights={brain.FollowRecovery.Flights};guard-threat={guard?.ProtectedThreatId ?? -1};guard-pressure={(guard?.RetainedPressure ?? 0f).ToString("0.000", CultureInfo.InvariantCulture)};guard-reason={guard?.CommitmentReason ?? "unavailable"};mine-job={mine?.JobId ?? 0};mine-policy={mine?.Policy.ToString() ?? "unavailable"};mine-status={mine?.Status ?? "unavailable"};mine-remaining={mine?.RemainingTiles ?? 0};mine-target={mine?.TargetTile?.ToString() ?? "-"};control-source={companion.Motor.ControlSource};position-evidence-tick={brain.Positioner.EvidenceTick};positions-evaluated={brain.Positioner.EvaluatedCandidates};reach-complete={senses.Reach.Complete};position-alternatives={brain.Positioner.CandidateEvidence};target-evidence-tick={companion.Arsenal.TargetEvidenceTick};target-evidence-age={senses.Tick - companion.Arsenal.TargetEvidenceTick};target-alternatives={companion.Arsenal.TargetEvidence}");
        SessionMap.Watch(
            MovementQueries.Tile(npc.Center),
            MovementQueries.Tile(senses.Player.Bottom),
            brain.Navigator.GoalTile,
            brain.Positioner.Chosen is Vector2 spot ? Vector2.Distance(npc.Center, spot) : float.MaxValue);

        if (!headerWritten)
        {
            var textColumns = new StringBuilder("# text_columns=state,action,reflex,top_threat,target,request,anchor,spot,lookahead,npc_tile,npc_px,npc_vel,wall_normal,liquid,held,weapon,fire,engage,torch,player_tile,spot_home,sample_phase,player_px,player_vel,player_liquid,player_hit,npc_hit,player_state,player_activity,player_support,control,control_source,desired_vel,follow_reason,recovery_reason,guard_reason,mine_policy,mine_status,mine_target,target_evidence,nav_status,position_reason,hunt_reason,hand_grant,control_request_owner,collection_method,mine_end_reason,attempt_end_activity,attempt_end_family,attempt_end_status,attempt_end_cause,attempt_end_attribution,pursuit_target,pursuit_evidence,aim_target,landed_hit_target,landed_hit_aimed,encounter_source,torch_reason,lighting_sites,intent_region,task_order,task_order_runner_up,evade_reason,evade_choice");
            // Offer columns are named from the registered activities, like the raw/final pairs, so
            // the declaration and the header cannot disagree about which activities exist.
            foreach (var a in brain.Chooser.Actions) textColumns.Append(',').Append(a.Name).Append("_offer");
            textColumns.Append(",meeting_reason,meeting_anchor,meeting_flood");
            textColumns.Append(",nav_failure,nav_failure_reason,nav_attempt_ending");
            textColumns.Append(",region_kind,region_anchor_px,region_player_px,region_comfort,region_work_tile,region_reach,region_arrival");
            // Lane A's textual columns, declared from the same activity list the header appends them from.
            foreach (var a in brain.Chooser.Actions)
                if (a is Activities.ICandidateFunnelSource) textColumns.Append(',').Append(a.Name).Append("_funnel");
            textColumns.Append(",torch_reference,torch_reference_dark,torch_reference_stage");
            writer.WriteLine(textColumns.ToString());
            var h = new StringBuilder();
            // A start timestamp is file metadata. Stopwatch is the observed wall duration of
            // every row; deriving wall time from game ticks would conceal pauses and lag.
            h.Append("tick\tstate\taction\treflex");
            foreach (var a in brain.Chooser.Actions)
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
            h.Append("\tnpc_tile\tnpc_px\tnpc_vel\ttouched_wall\twall_normal\twet\tliquid\tclearance\tmoved\tpinned\tdir\tlife\tself_danger\theld\tweapon\tshot\tfire\texp_bow\texp_knife\texp_target\tnear_threat\tweapon_reach\tengage\ttorch\tdark_near\tdark_ahead\ttorch_reason\tlight_samples\tlight_read_tick\tlight_region");
            h.Append("\tplayer_tile\tplayer_intent\tplayer_dead\tplayer_attacking\tplayer_chopping\tplayer_mining");
            h.Append("\tplan_ms\tflood_ms\tsenses_ms\treflex_ms\tdecide_ms\tposition_ms\tnavigate_ms\tbrain_ms\tclearance_builds\tstranded");
            // The reachability tier, which is where the companion decides whether to enter somewhere
            // it cannot leave and the one decision no offline pass can watch: how many tiles it can
            // reach, how many of those it can come home from, whether the spot it picked is one of
            // them, and whether the refusing flood was discarded because the player was outside it.
            h.Append("\treach_any\treach_two_way\treach_complete\tspot_home\tplayer_one_way");
            h.Append("\tguard_threat\tguard_pressure\tguard_reason\tmine_job\tmine_policy\tmine_status\tmine_remaining\tmine_target\ttarget_evidence_tick\ttarget_evidence_age\ttarget_evidence");
            // `control` is what the motor applied this tick and `desired_vel` the velocity it accelerated
            // toward after capping; the two differ where the request exceeded the cap.
            h.Append("\twall_elapsed_ms\tsample_phase\tplayer_px\tplayer_vel\tplayer_ground\tplayer_liquid\tplayer_life\tplayer_hit\tnpc_hit\tplayer_state\tplayer_activity\tplayer_support\tcontrol\tcontrol_source\tbrain_fresh\tdesired_vel");
            h.Append("\tnav_status\tposition_reason\tmovement_stalled\tattack_value\tattack_kills\tattack_harm\tweapon_cooldown\thunt_idle_ticks\thunt_reason");
            h.Append("\tchoice_fresh\tchoice_id\tchoice_tick");
            h.Append("\tcontrol_grant_fresh\tcontrol_grant_id\tcontrol_grant_tick\thand_grant\tcontrol_request_owner\tcontrol_motor_applications\tfinalise_ms");
            h.Append("\tplayer_intent_y\tplayer_intent_confidence\tplayer_intent_samples\tplayer_local_work_fraction");
            h.Append("\tmine_remaining_work_ticks\tmine_remaining_hits\tchop_remaining_work_ticks\tchop_remaining_hits");
            h.Append("\treunion_apart_ticks\treunion_departure\treunion_delay_cost_per_tick");
            h.Append("\tcollection_method");
            h.Append("\tmine_end_job\tmine_end_tick\tmine_end_reason\tmine_end_tracked\tmine_end_present\tmine_end_changed\tmine_end_missing\tmine_end_unobserved\tmine_end_companion_removed_sites\tmine_end_observed_clear");
            h.Append("\tactivity_attempt_id\tattempt_end_id\tattempt_end_activity_id\tattempt_end_activity\tattempt_end_family\tattempt_end_status\tattempt_end_cause\tattempt_end_attribution\tattempt_end_effects\tattempt_end_start_tick\tattempt_end_tick");
            foreach (var a in brain.Chooser.Actions)
                h.Append('\t').Append(a.Name).Append("_offer");
            foreach (var family in Enum.GetValues<Infrastructure.Selection.PurposeFamily>())
            {
                string name = family.ToString().ToLowerInvariant();
                h.Append('\t').Append(name).Append("_prepared\t").Append(name).Append("_deferred\t").Append(name).Append("_prepare_ms");
            }
            h.Append("\tmeeting_reason\tmeeting_anchor\tmeeting_player_ticks\tmeeting_companion_ticks\tmeeting_candidates\tmeeting_priced\tmeeting_flood");
            h.Append("\tnav_failure\tnav_failure_reason\tnav_failure_search_id\tnav_failure_attempt_id\tnav_attempt_ending\tnav_attempts_completed\tnav_attempts_failed\tnav_attempts_preempted\tnav_attempts_cancelled");
            h.Append("\tpursuit_target\tpursuit_value\tpursuit_access_ticks\tpursuit_evidence\taim_target\tlanded_hit_target\tlanded_hit_aimed\tlanded_hit_damage\tlanded_hit_tick\tlanded_hits\tguard_removal_ticks\tguard_usefulness\ttop_threat_effective_player\ttop_threat_effective_companion\tencounter_intensity\tencounter_source\tencounter_recognised\tencounter_pressure_ticks\tguard_access_ticks");
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
            // How many lights somebody was carrying inside the light field's window this tick — the player's
            // torch, a pet, a helmet, anything a mod adds — each of which the field discounted rather than
            // reading as the room's. Appended at the end and the schema left where it is, because nothing
            // before it moved and the reader addresses columns by name. It is here because the failure it
            // describes is otherwise invisible in a capture: a companion that walks a lit-looking passage
            // beside a torch-carrying player and places nothing looks exactly like one with nothing to do.
            h.Append("\ttransient_lights");
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
            // Lane A, schema 0.35.0, appended at the end so every column before it keeps its index. The time factor per
            // activity; the stage that refused each funnel's furthest candidate; the player's own smart-cursor torch tile,
            // its light, its placement reading and the stage lighting refuses it at; and this tick's collections.
            foreach (var a in brain.Chooser.Actions) h.Append('\t').Append(a.Name).Append("_time");
            foreach (var a in brain.Chooser.Actions)
                if (a is Activities.ICandidateFunnelSource) h.Append('\t').Append(a.Name).Append("_funnel");
            h.Append("\ttorch_reference\ttorch_reference_light\ttorch_reference_dark\ttorch_reference_stage\tgc0\tgc1\tgc2");
            lastGc0 = lastGc1 = lastGc2 = -1;
            // Lane C (the evade layer), appended after lane A's block: why the layer kept or bent this tick's controls, the
            // lookahead tick at which the job's own flight met a hit (-1 for never), which candidate a bent tick flew, and how
            // many candidates each refusal removed. `evade_reason` and `evade_choice` are textual and declared in the preamble.
            h.Append("\tevade_reason\tevade_hit_tick\tevade_choice\tevade_refused_nowhere\tevade_refused_danger");
            writer.WriteLine(h.ToString());
            headerWritten = true;
        }

        var sb = new StringBuilder(400);
        sb.Append(Main.GameUpdateCount);
        // Read player death from the live player rather than a cached observation. The brain now
        // continues after death, but this remains the authoritative lifecycle state for the row.
        sb.Append('\t').Append(companion.IsDowned ? "downed" : Main.LocalPlayer.dead ? "player-dead" : "up");
        sb.Append('\t').Append(brain.LastAction?.Name ?? "-");
        sb.Append('\t').Append(brain.Reflexes.Active ?? "-");
        foreach (var a in brain.Chooser.Actions)
        {
            float raw = 0f, fin = 0f;
            foreach (var s in brain.Chooser.LastScores)
                if (ReferenceEquals(s.Action, a)) { raw = s.Raw; fin = s.Final; break; }
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
        sb.Append('\t').Append(companion.Arsenal.LastChosen?.Name ?? "-");
        sb.Append('\t').Append(companion.Arsenal.LastShotSolved ? 1 : 0);
        // Why no projectile left the hands, which the shot flag alone cannot say: a reload and a
        // target with no reachable arc both read as a zero there, and they want opposite fixes.
        sb.Append('\t').Append(companion.Arsenal.LastFireOutcome);
        // Both weapons' expected damage, the rejected one included, so the choice can be read back
        // instead of re-derived: a row where the loser scored higher is a defect with no other tell.
        sb.Append('\t').Append(companion.Arsenal.LastPrimaryExpected.ToString("0.0"));
        sb.Append('\t').Append(companion.Arsenal.LastSecondaryExpected.ToString("0.0"));
        sb.Append('\t').Append(companion.Arsenal.LastTargetExpected.ToString("0.0"));
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
        sb.Append('\t').Append((companion.Arsenal.MaxReach / 16f).ToString("0.0", CultureInfo.InvariantCulture));
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

        sb.Append('\t').Append(guard?.ProtectedThreatId ?? -1);
        sb.Append('\t').Append((guard?.RetainedPressure ?? 0f).ToString("0.000", CultureInfo.InvariantCulture));
        sb.Append('\t').Append(guard?.CommitmentReason ?? "unavailable");
        sb.Append('\t').Append(mine?.JobId ?? 0);
        sb.Append('\t').Append(mine?.Policy.ToString() ?? "unavailable");
        sb.Append('\t').Append(mine?.Status ?? "unavailable");
        sb.Append('\t').Append(mine?.RemainingTiles ?? 0);
        sb.Append('\t').Append(mine?.TargetTile?.ToString() ?? "-");
        sb.Append('\t').Append(companion.Arsenal.TargetEvidenceTick);
        sb.Append('\t').Append(senses.Tick - companion.Arsenal.TargetEvidenceTick);
        sb.Append('\t').Append(companion.Arsenal.TargetEvidence);

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
        sb.Append('\t').Append(companion.Arsenal.LastAttackValue.ToString("0.000", CultureInfo.InvariantCulture));
        sb.Append('\t').Append(companion.Arsenal.LastExpectedKills).Append('\t').Append(companion.Arsenal.LastPreventedHarm.ToString("0.000", CultureInfo.InvariantCulture));
        sb.Append('\t').Append(companion.Arsenal.CooldownTicks).Append('\t').Append(hunt?.NoProgressTicks ?? 0).Append('\t').Append(hunt?.LastRejection ?? "unavailable");
        sb.Append('\t').Append(choiceEvaluated ? 1 : 0).Append('\t').Append(brain.Chooser.EvaluationId)
            .Append('\t').Append(brain.Chooser.EvaluationTick?.ToString(CultureInfo.InvariantCulture) ?? "-1");
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
        sb.Append('\t').Append(brain.Chooser.Reunion.ApartTicks)
            .Append('\t').Append(brain.Chooser.Reunion.Departure.ToString("0.000", CultureInfo.InvariantCulture))
            .Append('\t').Append(brain.Chooser.Reunion.DelayCostPerTick.ToString("0.000000", CultureInfo.InvariantCulture));
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
        var owner = brain.Chooser.Activity;
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
        // The retained board's classification, keyed like the raw/final pairs; an activity absent
        // from the latest comparison says so rather than borrowing an eligibility it never received.
        foreach (var a in brain.Chooser.Actions)
        {
            string offer = "not-compared";
            foreach (var s in brain.Chooser.LastScores)
                if (ReferenceEquals(s.Action, a)) { offer = s.Eligibility + ":" + s.EligibilityReason; break; }
            sb.Append('\t').Append(offer);
        }
        // Retained from the last completed comparison, like the score board; -1 before any.
        foreach (var family in Enum.GetValues<Infrastructure.Selection.PurposeFamily>())
        {
            int index = Array.FindIndex(brain.Chooser.Queries.LastFamilies, f => f.Family == family);
            if (index < 0) { sb.Append("\t-1\t-1\t-1"); continue; }
            var queries = brain.Chooser.Queries.LastFamilies[index];
            sb.Append('\t').Append(queries.Prepared).Append('\t').Append(queries.Deferred)
                .Append('\t').Append(queries.Milliseconds.ToString("0.000", CultureInfo.InvariantCulture));
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
        sb.Append('\t').Append(Identity(hunt?.Target?.Npc));
        sb.Append('\t').Append((hunt?.PursuitValue ?? 0f).ToString("0.000", CultureInfo.InvariantCulture));
        sb.Append('\t').Append((hunt?.PursuitAccessTicks ?? 0f).ToString("0.0", CultureInfo.InvariantCulture));
        sb.Append('\t').Append(string.IsNullOrEmpty(hunt?.PursuitEvidence) ? "-" : hunt!.PursuitEvidence);
        sb.Append('\t').Append(Identity(brain.EngageTarget));
        var landed = Weapons.TrackLandedHits.Last;
        sb.Append('\t').Append(landed is { } hit ? string.Create(CultureInfo.InvariantCulture, $"{hit.HitSlot}:{hit.HitGeneration}") : "-");
        sb.Append('\t').Append(landed is { } aimed ? string.Create(CultureInfo.InvariantCulture, $"{aimed.AimSlot}:{aimed.AimGeneration}") : "-");
        sb.Append('\t').Append(landed?.Damage ?? 0);
        sb.Append('\t').Append(landed?.Tick.ToString(CultureInfo.InvariantCulture) ?? "-1");
        sb.Append('\t').Append(Weapons.TrackLandedHits.Count);
        float removal = guard?.RemovalTicks ?? float.PositiveInfinity;
        sb.Append('\t').Append(float.IsFinite(removal) ? removal.ToString("0.0", CultureInfo.InvariantCulture) : "-1");
        sb.Append('\t').Append((guard?.InterventionUsefulness ?? 1f).ToString("0.000", CultureInfo.InvariantCulture));
        sb.Append('\t').Append((top?.EffectiveDamageToPlayer ?? 0f).ToString("0.0", CultureInfo.InvariantCulture));
        sb.Append('\t').Append((top?.EffectiveDamageToCompanion ?? 0f).ToString("0.0", CultureInfo.InvariantCulture));
        var encounter = brain.Senses.Encounter;
        sb.Append('\t').Append(encounter.Intensity.ToString("0.000", CultureInfo.InvariantCulture));
        sb.Append('\t').Append(encounter.Source);
        sb.Append('\t').Append(encounter.Recognised ? 1 : 0);
        sb.Append('\t').Append(encounter.PressureTicks);
        // The firing access guard's share counted: zero from here, the walk after moving, -1 for an
        // unsettled region, a proven absence or a threat nothing can damage (read the share beside it).
        float access = guard?.AccessTicks ?? float.NaN;
        sb.Append('\t').Append(float.IsFinite(access) ? access.ToString("0.0", CultureInfo.InvariantCulture) : "-1");
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
        sb.Append('\t').Append(Observation.TransientLights.Count);
        var lighting = brain.Chooser.Actions.OfType<Activities.NearbyAssistance.LightUsefulArea>().FirstOrDefault();
        sb.Append('\t').Append(string.IsNullOrEmpty(lighting?.LastSearchSites) ? "-" : lighting!.LastSearchSites)
            .Append('\t').Append(lighting?.LastSearchAsked ?? 0);
        sb.Append('\t').Append(Movement.TerrainChanges.Revision);
        var intent = senses.Intent.Region;
        sb.Append('\t').Append(FormattableString.Invariant(
                $"{intent.Centre.X:0},{intent.Centre.Y:0};{intent.HalfSize.X:0},{intent.HalfSize.Y:0}"))
            .Append('\t').Append(intent.Pull(companion.NPC.Bottom).ToString("0.000", CultureInfo.InvariantCulture));
        sb.Append('\t').Append(brain.Chooser.LastTaskOrder.Length == 0 ? "-" : brain.Chooser.LastTaskOrder)
            .Append('\t').Append(brain.Chooser.LastTaskOrderRunnerUp.Length == 0 ? "-" : brain.Chooser.LastTaskOrderRunnerUp);
        // Lane A, schema 0.35.0: the time factor each activity's final carried, one where the activity was not compared.
        foreach (var a in brain.Chooser.Actions)
        {
            float time = 1f;
            foreach (var s in brain.Chooser.LastScores)
                if (ReferenceEquals(s.Action, a)) { time = s.Time; break; }
            sb.Append('\t').Append(time.ToString("0.000", CultureInfo.InvariantCulture));
        }
        // Each funnel's furthest candidate's refusing stage, and the funnel as an occurrence when its outcome changed.
        foreach (var a in brain.Chooser.Actions)
            if (a is Activities.ICandidateFunnelSource source)
            {
                var funnel = source.Funnel;
                string stage = funnel.BestStage, summary = funnel.Summary();
                sb.Append('\t').Append(stage);
                if (GodsEyeEvents.CandidateFunnelChanged(npc, a.Name, stage, summary))
                    GodsEyeEvents.RecordCandidateFunnel(npc, a.Name, stage, summary, funnel.Total, funnel.Describe());
            }
        // The player's own smart cursor as the reference lighting is judged against: the tile it would offer him, that
        // tile's own light, what that light says to placing a torch, and the stage at which lighting refuses the tile.
        var referenceReading = lighting?.PlayerReferenceReading;
        sb.Append('\t').Append(lighting?.PlayerReferenceTile is Point referenceTile ? FormattableString.Invariant($"{referenceTile.X},{referenceTile.Y}") : "-")
            .Append('\t').Append(referenceReading is { Light: not Observation.LightSense.PlacementLight.Unread } read
                ? read.Brightness.ToString("0.000", CultureInfo.InvariantCulture) : "-")
            .Append('\t').Append(referenceReading?.Light switch
            {
                Observation.LightSense.PlacementLight.Dark => "dark",
                Observation.LightSense.PlacementLight.Carried => "carried",
                Observation.LightSense.PlacementLight.Lit => "lit",
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
        sb.Append('\t').Append(evade.Reason switch { EvadeReason.Kept => "kept", EvadeReason.Hit => "hit", _ => "off" })
            .Append('\t').Append(evade.HitTick)
            .Append('\t').Append(!evade.Bent ? "-" : evade.Choice switch { EvadeChoice.Stop => "stop", EvadeChoice.JobHeading => "job-heading", _ => "heading" })
            .Append('\t').Append(evade.RefusedNowhere)
            .Append('\t').Append(evade.RefusedDanger);

        // A write that fails (disk full, a stream the OS closed) must not escape the NPC's AI
        // and take the companion with it; the record stops and the game goes on.
        try
        {
            writer.WriteLine(sb.ToString());
            rowsWritten++;
            if (++sinceFlush >= FlushEveryTicks)
            {
                sinceFlush = 0;
                writer.Flush();
                GodsEyeEvents.Flush();
            }
        }
        catch (Exception e)
        {
            ModContent.GetInstance<AICompanion>().Logger.Error($"BrainTelemetry: write failed, recording stops: {e.Message}");
            try { writer.Dispose(); } catch { /* the stream is already broken */ }
            writer = null;
        }
        lastRecordMs = recordClock.Elapsed.TotalMilliseconds;
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
            TileShape.Platform => passThrough ? "platform" : "solid-platform",
            TileShape.Solid => "solid",
            _ => "air",
        };
    }

    internal static string DescribeControls(Controls controls)
        => $"desired={controls.Desired.X.ToString("0.00", CultureInfo.InvariantCulture)},{controls.Desired.Y.ToString("0.00", CultureInfo.InvariantCulture)}";

    /// <summary>The motor's liquid kind by name, in the game's own numbering: water, lava, honey, shimmer; -1 is dry.</summary>
    private static string LiquidName(int kind) => kind switch { 0 => "water", 1 => "lava", 2 => "honey", 3 => "shimmer", _ => "dry" };
}
