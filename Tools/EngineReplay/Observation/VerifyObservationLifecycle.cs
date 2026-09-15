extern alias live;

using System.Reflection;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;
using BrainTelemetry = live::AICompanion.Companion.Brain.Infrastructure.Diagnostics.BrainTelemetry;

/// <summary>
/// Exercises the real telemetry writer without a game window. It proves what the recorder can
/// actually write at world entry and across its own callbacks; it deliberately does not claim
/// that Terraria completed the surrounding world load or persisted the surrounding save.
/// </summary>
internal static class VerifyObservationLifecycle
{
    public static int Run()
    {
        FieldInfo savePath = typeof(Terraria.Program).GetField("SavePath", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Terraria save-path backing field is unavailable");
        object? priorSavePath = savePath.GetValue(null);
        var priorMiningPolicy = live::AICompanion.Companion.Brain.Activities.WorkPolicies.Mining;
        string root = Path.Combine(Path.GetTempPath(), "aic-observation-lifecycle-" + Guid.NewGuid().ToString("N"));
        savePath.SetValue(null, root);
        try
        {
            VerifySameStemGainsAnAttemptSuffix();
            VerifyZeroTickLifecycleMetadata();
            VerifyRecordingSwitch();
            VerifyInspectorGeometry();
            VerifyNotchOpeningConsumesThePress();
            VerifyCompanionHud.Verify();
            VerifyOneCompleteSample();
            VerifyEndedOreJobRecording();
            VerifyRecoveryDoesNotRefreshTheChoice();
            VerifySafetyWithNoOrdinaryOffer();
            VerifyAttemptEvidenceProducers.Run();
            Console.WriteLine("observation lifecycle: reserved retry names, zero-tick metadata and callback-scoped lifecycle evidence passed");
            return 0;
        }
        catch (Exception error)
        {
            AICompanion.Tools.Ledger.EmitLedgerRows.Detail("observation lifecycle: " + error);
            return 1;
        }
        finally
        {
            Close();
            live::AICompanion.Companion.Brain.Activities.WorkPolicies.Mining = priorMiningPolicy;
            savePath.SetValue(null, priorSavePath);
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static void VerifyOneCompleteSample()
    {
        var recorder = new BrainTelemetry(); Attach(recorder);
        var companion = VerifyCompanionLifecycle.Create();
        // This is a new observation scenario. Native world callbacks reset and age
        // tool contacts; otherwise a prior fixture's ore hit lasts forever here.
        var workClock = new live::AICompanion.Companion.Brain.Infrastructure.Observation.TileDamageClock();
        workClock.OnWorldLoad();
        Main.LocalPlayer.dead = false;
        Main.LocalPlayer.velocity = new Microsoft.Xna.Framework.Vector2(0, -1);
        for (int tick = 0; tick < 120; tick++)
        {
            Main.LocalPlayer.position += Main.LocalPlayer.velocity;
            VerifyObservedMotion.SetTick(Main.GameUpdateCount + 1);
            companion.Brain.Senses.Player.Update(Main.LocalPlayer, companion.NPC);
            workClock.PostUpdateEverything();
        }
        recorder.OnWorldLoad();
        string path = Directory.GetFiles(BrainTelemetry.Folder, "*.tsv").OrderByDescending(File.GetLastWriteTimeUtc).First();
        VerifyObservedMotion.SetTick(Main.GameUpdateCount + 1);
        VerifyCompanionLifecycle.TickWithOneControlGrant(companion); recorder.OnWorldUnload();
        string[] lines = File.ReadAllLines(path);
        int header = Array.FindIndex(lines, l => l.StartsWith("tick\t"));
        Require(header >= 0 && header + 1 < lines.Length, "real sample writer emitted no table row");
        string[] names = lines[header].Split('\t'), values = lines[header + 1].Split('\t');
        Require(names.Length == values.Length, $"sample/header widths disagree: {names.Length}/{values.Length}");
        // `hurting` where the walker's list named `head_submerged`. That column was Terraria's own drowning
        // rectangle over a forty-two-pixel body, and this body has no head; what the recorder carries instead is
        // whether the liquid the circle is touching is liquid that hurts it, which is the same fact the escape
        // fixtures read off the motor. The name is checked rather than assumed because the schema appends
        // columns and a reader that names them survives a bump — but only if it names ones that exist.
        foreach (string name in new[] { "escape_stage", "state_search_pending", "hurting", "attack_value", "hunt_reason", "nav_status",
            "player_intent_y", "player_intent_confidence", "player_intent_samples", "player_local_work_fraction", "collection_method" })
            Require(Array.IndexOf(names, name) >= 0, "causal sample field missing: " + name);
        float Number(string name) => float.Parse(values[Array.IndexOf(names, name)], System.Globalization.CultureInfo.InvariantCulture);
        Require(Number("player_intent_y") < -.8f && Number("player_intent_confidence") > .9f
            && Number("player_intent_samples") > 100,
            $"the actual recorder must preserve supported vertical intent, not empty placeholders: y={Number("player_intent_y")}; confidence={Number("player_intent_confidence")}; samples={Number("player_intent_samples")}; local-work={Number("player_local_work_fraction")}; ore={companion.Brain.Senses.Player.MinedOre}; tree={companion.Brain.Senses.Player.ChoppedTree}");
        Require(lines.Any(l => l.StartsWith("# text_columns=")), "writer must declare its textual columns");
        string events = File.ReadAllText(Path.ChangeExtension(path, null) + "-events.jsonl");
        foreach (string family in new[] { "Gathering", "Combat", "NearbyAssistance" })
            Require(events.Contains("family:" + family + "=child:"), "decision writer omitted a family nomination: " + family);
        // The gravity trio is gone from this row, and deleted rather than loosened.
        //
        // It used to set `NPC.gravity` to 0.1234 by reflection and a different model gravity beside it, then
        // require the recorded event to carry both values and the tick they were read on — which proved that the
        // recorder read the engine's side and the model's side of the same tick instead of writing one of them
        // twice. That worked because the two numbers could differ. This body has no gravity on either side: the
        // engine only adds its velocity, and the recorder writes `gravity-observation-tick=-1;engine-gravity=0;
        // model-gravity=0;gravity-enabled=False` as fixed text to keep the columns a reader names. Asserting a
        // constant against itself is a row that cannot fail, which is worse than no row.
        //
        // The one-row-per-tick property the surrounding fixture exists for is not lost with it: the header and
        // sample widths, the single navigation-state event, the intent numbers and the three family nominations
        // above all still come from one recorded tick. What is no longer covered anywhere is the two-sided read
        // itself, and it is worth saying plainly that nothing replaces it, because the day this body gains any
        // per-tick engine quantity worth capturing, this is the row that should have caught a duplicated read.
        Require(File.ReadLines(Path.ChangeExtension(path, null) + "-events.jsonl")
                .Count(line => line.Contains("\"kind\":\"navigation-state\"", StringComparison.Ordinal)) == 1,
            "one AI update must record exactly one navigation-state event, never a duplicated read of the same tick");
    }

    private static void VerifyEndedOreJobRecording()
    {
        var (mine, ctx) = VerifyOreWork.SetUp(live::AICompanion.Companion.Brain.Activities.WorkPolicy.Opportunistic,
            TileID.Copper, new Microsoft.Xna.Framework.Point(25, 89));
        Require(VerifyPreparedActivities.PrepareAndScore(mine, ctx) > 0, "recorded conclusion needs a real admitted vein");
        int index = ctx.Companion.Brain.Chooser.Actions.FindIndex(action => action.Name == "mine");
        ctx.Companion.Brain.Chooser.Actions[index] = mine;
        live::AICompanion.Companion.Brain.Activities.WorkPolicies.Mining = live::AICompanion.Companion.Brain.Activities.WorkPolicy.Disabled;
        mine.Execute(ctx);
        var conclusion = mine.LastConclusion ?? throw new InvalidOperationException("revocation omitted the job conclusion");
        Require(conclusion is { Present: 1, ObservedClear: false }, "revocation must preserve remaining world work");
        var recorder = new BrainTelemetry(); Attach(recorder);
        recorder.OnWorldLoad();
        string path = Directory.GetFiles(BrainTelemetry.Folder, "*.tsv").OrderByDescending(File.GetLastWriteTimeUtc).First();
        VerifyObservedMotion.SetTick(Main.GameUpdateCount + 1);
        VerifyCompanionLifecycle.TickWithOneControlGrant(ctx.Companion);
        recorder.OnWorldUnload();
        string[] lines = File.ReadAllLines(path);
        int header = Array.FindIndex(lines, line => line.StartsWith("tick\t"));
        string[] names = lines[header].Split('\t'), values = lines[header + 1].Split('\t');
        Require(names.Length == values.Length, "job-end columns must preserve the complete sample shape");
        string Value(string name) => values[Array.IndexOf(names, name)];
        Require(Value("mine_end_job") == conclusion.JobId.ToString()
            && Value("mine_end_tick") == conclusion.Tick.ToString()
            && Value("mine_end_reason") == "disabled before execution"
            && Value("mine_end_tracked") == "1" && Value("mine_end_present") == "1"
            && Value("mine_end_missing") == "0" && Value("mine_end_changed") == "0"
            && Value("mine_end_companion_removed_sites") == "0" && Value("mine_end_observed_clear") == "0",
            "the actual recorder must preserve a cancelled job with remaining ore, not fabricate cleared work");
        string events = File.ReadAllText(Path.ChangeExtension(path, null) + "-events.jsonl");
        Require(events.Contains("mine-last-conclusion=") && events.Contains("disabled before execution"),
            "God's Eye must receive the same retained job conclusion");
        live::AICompanion.Companion.Brain.Activities.WorkPolicies.Mining = live::AICompanion.Companion.Brain.Activities.WorkPolicy.Opportunistic;
    }

    private static void VerifyRecoveryDoesNotRefreshTheChoice()
    {
        var recorder = new BrainTelemetry(); Attach(recorder);
        var companion = VerifyCompanionLifecycle.Create();
        // The lifecycle helper starts with a dead player. This scenario needs useful ordinary
        // work before recovery; otherwise there is no activity for recovery to suspend.
        Main.LocalPlayer.dead = false;
        recorder.OnWorldLoad();
        string path = Directory.GetFiles(BrainTelemetry.Folder, "*.tsv").OrderByDescending(File.GetLastWriteTimeUtc).First();
        VerifyObservedMotion.SetTick(Main.GameUpdateCount + 1);
        VerifyCompanionLifecycle.TickWithOneControlGrant(companion);
        // Start from an already active recovery so the actual coordinator takes its early
        // return before choosing. The owner stays inside the native fixture's world.
        Main.LocalPlayer.dead = false;
        Main.LocalPlayer.position = new Microsoft.Xna.Framework.Vector2(1100, 1398);
        typeof(live::AICompanion.Companion.Brain.SharedBehaviours.Recovery.RecoverDistantCompanion)
            .GetField("<Active>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(companion.Brain.FollowRecovery, true);
        VerifyObservedMotion.SetTick((Main.GameUpdateCount / 60 + 1) * 60);
        VerifyCompanionLifecycle.TickWithOneControlGrant(companion);
        Require(companion.Motor.ControlSource == "follow-recovery-flight", "fixture did not enter the actual recovery control path");
        Main.LocalPlayer.dead = true;
        VerifyObservedMotion.SetTick(Main.GameUpdateCount + 1);
        VerifyCompanionLifecycle.TickWithOneControlGrant(companion);
        Require(companion.Brain.Chooser.Current == null, "the empty post-recovery board must have no ordinary activity");
        Main.LocalPlayer.dead = false;
        Main.LocalPlayer.Bottom = companion.NPC.Bottom;
        VerifyObservedMotion.SetTick(Main.GameUpdateCount + 1);
        VerifyCompanionLifecycle.TickWithOneControlGrant(companion);
        companion.CheckDead();
        VerifyObservedMotion.SetTick(Main.GameUpdateCount + 1);
        VerifyCompanionLifecycle.TickWithOneControlGrant(companion);
        recorder.OnWorldUnload();
        string events = Path.ChangeExtension(path, null) + "-events.jsonl";
        string[] activities = File.ReadLines(events).Where(line => line.Contains("\"kind\":\"activity-state\"", StringComparison.Ordinal)).ToArray();
        Require(activities.Length == 5 && activities[0].Contains("phase=Executing")
            && activities[1].Contains("phase=Suspended;reason=follow-recovery-flight")
            && activities[2].Contains("phase=None") && activities[3].Contains("phase=Executing")
            && activities[4].Contains("phase=Suspended;reason=downed"),
            "the real recorder must distinguish execution, recovery suspension, no offer, resumed activity and downing; actual records: "
                + string.Join("\n", activities));
        string[] attempts = File.ReadLines(events).Where(line => line.Contains("\"kind\":\"attempt-outcome\"", StringComparison.Ordinal)).ToArray();
        Require(attempts.Length == 2
            && attempts[0].Contains("status=Interrupted;attribution=NotApplicable;cause=follow-recovery-flight")
            && attempts[1].Contains("status=Interrupted;attribution=NotApplicable;cause=downed"),
            "recovery and downing must each close their own attempt as an interruption, and the empty board between them must not invent one; actual records: "
                + string.Join("\n", attempts) + "\nretained by the brain: "
                + string.Join("\n", companion.Brain.Chooser.Activity.RecentAttempts));
        string recoveryEvent = File.ReadLines(events).Single(line => line.Contains("\"kind\":\"decision\"", StringComparison.Ordinal)
            && line.Contains("control-source=follow-recovery-flight", StringComparison.Ordinal));
        using var recorded = System.Text.Json.JsonDocument.Parse(recoveryEvent);
        string detail = recorded.RootElement.GetProperty("detail").GetString()!;
        Require(!detail.Contains("freshness=fresh", StringComparison.Ordinal) && detail.Contains("brain-fresh=True"),
            "recovery skipped selection but the real recorder labelled its retained score board fresh");
        Require(detail.Contains("activity-phase=Suspended;activity-reason=follow-recovery-flight"),
            "a retained decision must expose that recovery suspended its activity");
        string recoveryNavigation = File.ReadLines(events).Single(line => line.Contains("\"kind\":\"navigation-state\"", StringComparison.Ordinal)
            && line.Contains("control-source=follow-recovery-flight", StringComparison.Ordinal));
        using var navigation = System.Text.Json.JsonDocument.Parse(recoveryNavigation);
        string navigationDetail = navigation.RootElement.GetProperty("detail").GetString()!;
        Require(navigationDetail.Contains("activity-phase=Suspended;activity-reason=follow-recovery-flight")
            && navigationDetail.Contains("activity-id="),
            "movement evidence must retain the primary activity identity without attributing recovery to ordinary execution");
        string[] lines = File.ReadAllLines(path);
        int header = Array.FindIndex(lines, line => line.StartsWith("tick\t"));
        string[] names = lines[header].Split('\t');
        string[][] rows = lines.Skip(header + 1).Where(line => !line.StartsWith('#')).Select(line => line.Split('\t')).ToArray();
        Require(rows.Length == 5 && rows.All(row => row.Length == names.Length), "freshness fixture must preserve all five complete samples");
        string Value(int row, string name)
        {
            int column = Array.IndexOf(names, name);
            Require(column >= 0, "missing decision evidence column: " + name);
            return rows[row][column];
        }
        Require(Value(0, "choice_fresh") == "1" && Value(1, "choice_fresh") == "0" && Value(1, "brain_fresh") == "1",
            "selection and brain execution must be separate observations");
        Require(Value(0, "choice_id") == Value(1, "choice_id") && Value(0, "choice_tick") == Value(1, "choice_tick"),
            "recovery must preserve the identity and source time of the earlier comparison");
        Require(Value(2, "choice_fresh") == "1" && long.Parse(Value(2, "choice_id")) == long.Parse(Value(0, "choice_id")) + 1,
            "resumed selection must publish a new comparison even when no activity is worthwhile");
        Require(Value(3, "choice_fresh") == "1" && long.Parse(Value(3, "choice_id")) == long.Parse(Value(2, "choice_id")) + 1,
            "a newly useful companionship activity must come from a fresh comparison");
        Require(Value(4, "choice_fresh") == "0" && Value(4, "brain_fresh") == "0" && Value(4, "choice_id") == Value(3, "choice_id"),
            "downing must not refresh a retained comparison");
        for (int row = 0; row < rows.Length; row++)
        {
            Require(Value(row, "control_grant_fresh") == "1" && Value(row, "control_motor_applications") == "1",
                "every ordinary, recovery and downed tick must record one fresh motor grant");
            Require(Value(row, "control_grant_tick") == Value(row, "tick"), "grant tick must describe this sample");
            if (row > 0) Require(long.Parse(Value(row, "control_grant_id")) == long.Parse(Value(row - 1, "control_grant_id")) + 1,
                "control grant identity must advance even while the retained choice does not");
        }
        Require(Value(1, "control_request_owner") == "follow-recovery-flight" && Value(1, "hand_grant") == "Available",
            "recovery must preserve the independently available hand");
        Require(Value(4, "control_request_owner") == "downed" && Value(4, "hand_grant") == "Unavailable",
            "downed movement must not permit weapon use");
        string[] grants = File.ReadLines(events).Where(line => line.Contains("\"kind\":\"control-grant\"", StringComparison.Ordinal)).ToArray();
        Require(grants.Length == 5 && grants[1].Contains("follow-recovery-flight") && grants[4].Contains("Unavailable"),
            "sparse grant evidence must preserve all five ownership transitions");
    }

    private static void VerifySafetyWithNoOrdinaryOffer()
    {
        var (_, context) = VerifyOreWork.SetUp(live::AICompanion.Companion.Brain.Activities.WorkPolicy.Disabled,
            TileID.Copper, new Microsoft.Xna.Framework.Point(25, 89));
        var companion = context.Companion;
        companion.Brain.Chooser.Actions.Clear();
        var recorder = new BrainTelemetry(); Attach(recorder);
        recorder.OnWorldLoad();
        string path = Directory.GetFiles(BrainTelemetry.Folder, "*.tsv").OrderByDescending(File.GetLastWriteTimeUtc).First();
        // The liquid is poured before the first recorded tick rather than between the first and the second,
        // and the three rows this fixture asserts are unchanged by the move. The orb has no engine collision,
        // so nothing sets `npc.wet` for it: the motor's `ReadLiquid` decides the liquid from the circle's own
        // contact and then writes `wet`, `lavaWet` and the rest itself, which means a hand-set `wet` was both
        // ignored and overwritten. It runs inside `Commit`, at the end of a tick, so `Motor.InHurtingLiquid`
        // read at the top of the next tick describes where the body was when it was last moved. Pouring the
        // liquid and asserting on the very next tick therefore asked the brain about a fact no Commit had
        // published yet, and read the one-tick publication order as an absent danger.
        //
        // So the first recorded tick is the one that discovers the liquid and still reports no safety — which
        // is what row 0 asserts — and the second is the one that acts on it, which is row 1. The row contract
        // is the same three rows as before and the scene is the same scene; what changed is that the fact is
        // published by its producer instead of arranged by the fixture.
        for (int x = 18; x <= 22; x++)
        for (int y = 84; y <= 89; y++) Main.tile[x, y].LiquidAmount = byte.MaxValue;
        live::AICompanion.Companion.Brain.Infrastructure.Movement.TerrainChanges.Reset();
        VerifyObservedMotion.SetTick(Main.GameUpdateCount + 1);
        VerifyCompanionLifecycle.TickWithOneControlGrant(companion);
        VerifyObservedMotion.SetTick(Main.GameUpdateCount + 1);
        VerifyCompanionLifecycle.TickWithOneControlGrant(companion);
        Require(companion.Brain.Safety.Active && companion.Brain.Chooser.Current == null,
            "native environmental danger must create safety ownership with no ordinary activity; "
            + $"centre={companion.NPC.Center} liquidKind={companion.Motor.LiquidKind} "
            + $"hurting={companion.Motor.InHurtingLiquid} safety={companion.Brain.Safety.Active}/{companion.Brain.Safety.Kind} "
            + $"activity={companion.Brain.Chooser.Current?.Name ?? "<none>"}");
        companion.CheckDead();
        VerifyObservedMotion.SetTick(Main.GameUpdateCount + 1);
        VerifyCompanionLifecycle.TickWithOneControlGrant(companion);
        recorder.OnWorldUnload();
        string[] lines = File.ReadAllLines(path);
        int header = Array.FindIndex(lines, line => line.StartsWith("tick\t"));
        string[] names = lines[header].Split('\t');
        string[][] rows = lines.Skip(header + 1).Where(line => !line.StartsWith('#')).Select(line => line.Split('\t')).ToArray();
        Require(rows.Length == 3 && rows.All(row => row.Length == names.Length), "safety fixture must write all three complete samples");
        string Value(int row, string name) => rows[row][Array.IndexOf(names, name)];
        Require(Value(0, "safety_active") == "0" && Value(1, "safety_active") == "1"
            && Value(1, "safety_kind") == "environmental-escape" && long.Parse(Value(1, "safety_response_id")) > 0,
            "the actual recorder must expose a separate environmental response identity");
        Require(Value(1, "choice_fresh") == "0" && Value(1, "control_grant_fresh") == "1"
            && Value(1, "choice_id") == Value(0, "choice_id"),
            "shared safety must not fabricate an ordinary choice comparison");
        Require(Value(2, "safety_active") == "0" && Value(2, "safety_last_end") == "downed"
            && Value(2, "hand_grant") == "Unavailable", "downing must explicitly cancel shared safety and hand ownership");
    }

    private static void VerifySameStemGainsAnAttemptSuffix()
    {
        MethodInfo reserve = typeof(BrainTelemetry).GetMethod("ReserveSessionPath", BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("recorder no longer exposes its reservation implementation");
        object?[] firstArguments = { "engine-replay-same-second", null };
        string first = (string)(reserve.Invoke(null, firstArguments) ?? throw new InvalidOperationException("first reservation returned no path"));
        Close();
        object?[] secondArguments = { "engine-replay-same-second", null };
        string second = (string)(reserve.Invoke(null, secondArguments) ?? throw new InvalidOperationException("second reservation returned no path"));
        Close();
        Require(Path.GetFileNameWithoutExtension(first) == "engine-replay-same-second", "first explicit reservation gained an unnecessary suffix");
        Require(Path.GetFileNameWithoutExtension(second) == "engine-replay-same-second-1", "same-stem retry overwrote evidence instead of using attempt suffix 1");
    }

    private static void VerifyZeroTickLifecycleMetadata()
    {
        var recorder = new BrainTelemetry();
        Attach(recorder);
        recorder.OnWorldLoad();
        string folder = BrainTelemetry.Folder;
        string tsv = Directory.GetFiles(folder, "*.tsv").OrderByDescending(File.GetLastWriteTimeUtc).First();
        // No Record call occurs before this check: the world-entry metadata must survive an
        // empty session. These callbacks are deliberately invoked directly to test their own
        // recorded scope rather than Terraria's outer save/load completion.
        recorder.LoadWorldData(new TagCompound());
        recorder.PostUpdateEverything();
        recorder.SaveWorldData(new TagCompound());
        recorder.OnWorldUnload();

        string contents = File.ReadAllText(tsv);
        Require(contents.Contains("# schema=", StringComparison.Ordinal) && contents.Contains("# lifecycle=world-entry-observed", StringComparison.Ordinal),
            "a zero-tick world session did not retain flushed recorder metadata");
        Require(!contents.Contains("\ntick\t", StringComparison.Ordinal), "zero-tick session wrote a sample header without a sample");
        Require(contents.Contains("# terraria=") && contents.Contains("tml_assembly=") && contents.Contains("# mods="),
            "bug report metadata must carry the game, loader and loaded mod versions");
        string events = Path.ChangeExtension(tsv, null) + "-events.jsonl";
        string eventText = File.ReadAllText(events);
        Require(eventText.Contains("\"label\":\"world-entry\"", StringComparison.Ordinal)
            && eventText.Contains("\"label\":\"tag-load-returned\"", StringComparison.Ordinal)
            && eventText.Contains("\"label\":\"first-update\"", StringComparison.Ordinal)
            && eventText.Contains("\"label\":\"save-returned\"", StringComparison.Ordinal),
            "lifecycle callbacks did not write their actual observed phases");
        Require(eventText.Contains("outer-load=unobservable", StringComparison.Ordinal)
            && eventText.Contains("world-persisted=unobservable", StringComparison.Ordinal),
            "lifecycle evidence overclaimed outer load or save completion");
    }

    private static void Close()
    {
        // The recorder's Close takes the closure reason it writes into the end marker; a fixture closing it directly names
        // itself rather than borrowing a gameplay reason such as world-unload or recording-disabled.
        typeof(BrainTelemetry).GetMethod("Close", BindingFlags.Static | BindingFlags.NonPublic)?.Invoke(null, new object[] { "fixture-close" });
    }

    private static void VerifyNotchOpeningConsumesThePress()
    {
        bool menu = Main.gameMenu, oldLeft = Main.mouseLeft;
        int oldX = Main.mouseX, oldY = Main.mouseY;
        var companion = VerifyCompanionLifecycle.Create();
        if (ModContent.GetInstance<live::AICompanion.Companion.CharacterBody.CompanionNPC>() == null) ContentInstance.Register(companion);
        companion.NPC.type = ModContent.NPCType<live::AICompanion.Companion.CharacterBody.CompanionNPC>();
        companion.NPC.active = true;
        Main.npc[0] = companion.NPC;
        var owner = Main.LocalPlayer.GetModPlayer<live::AICompanion.Companion.PlayerIntegration.CompanionPlayer>();
        try
        {
            Main.gameMenu = false;
            var box = live::AICompanion.Companion.HeadsUpDisplay.CompanionHealthBar.Bounds(owner);
            foreach (int x in new[] { box.Left + 1, box.Center.X, box.Right - 1 })
            {
                Main.mouseX = x; Main.mouseY = box.Center.Y; Main.mouseLeft = true;
                Main.LocalPlayer.mouseInterface = false;
                // No Draw has run: both icon wings must consume the opening press too.
                owner.PreUpdate();
                Require(Main.LocalPlayer.mouseInterface, "the notch and both icon wings must capture before item use");
            }
        }
        finally { Main.gameMenu = menu; Main.mouseX = oldX; Main.mouseY = oldY; Main.mouseLeft = oldLeft; Main.npc[0].active = false; }
    }

    private static void VerifyRecordingSwitch()
    {
        var config = new live::AICompanion.Companion.DiagnosticsConfiguration.CompanionDiagnosticsConfig();
        ContentInstance.Register(config);
        var recorder = new BrainTelemetry(); Attach(recorder);
        config.RecordTelemetry = false;
        config.OnChanged();
        int before = Directory.GetFiles(BrainTelemetry.Folder).Length;
        recorder.OnWorldLoad();
        Require(Directory.GetFiles(BrainTelemetry.Folder).Length == before, "recording disabled must create no session files");
        config.RecordTelemetry = true;
        recorder.OnWorldLoad();
        string newest = Directory.GetFiles(BrainTelemetry.Folder, "*.tsv").OrderByDescending(File.GetLastWriteTimeUtc).First();
        var subject = new NPC { whoAmI = 77, type = NPCID.BlueSlime, width = 20, height = 20, noGravity = true, noTileCollide = true };
        live::AICompanion.Companion.Brain.Infrastructure.Observation.PredictObservedMotion.Observe(subject);
        var forecast = live::AICompanion.Companion.Brain.Infrastructure.Observation.PredictObservedMotion.ExistingForecast(subject);
        Require(forecast.Count > 0, "fixture must contain an active gameplay forecast");
        config.RecordTelemetry = false; config.OnChanged();
        Require(ReferenceEquals(forecast, live::AICompanion.Companion.Brain.Infrastructure.Observation.PredictObservedMotion.ExistingForecast(subject)),
            "turning off telemetry must preserve gameplay prediction state");
        using (File.Open(newest, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
        long bytes = new FileInfo(newest).Length;
        recorder.PostUpdateEverything();
        Require(new FileInfo(newest).Length == bytes, "disabled capture must not append samples");
        config.RecordTelemetry = true;
        config.EnableBrainInspector = false; config.OnChanged();
        Require(!live::AICompanion.Companion.Brain.Infrastructure.Diagnostics.BrainOverlay.MayCapture,
            "disabled inspector must not retain simulation traces");
        config.EnableBrainInspector = true;
    }

    private static void VerifyInspectorGeometry()
    {
        foreach (var viewport in new[] { (640, 360), (800, 600), (1280, 720), (1920, 1080) })
        foreach (float scale in new[] { 1f, 1.25f, 1.5f })
        {
            int width = (int)(viewport.Item1 / scale), height = (int)(viewport.Item2 / scale);
            var bounds = live::AICompanion.Companion.Brain.Infrastructure.Diagnostics.BrainOverlay.PanelBounds(width, height);
            Require(bounds.Left >= 0 && bounds.Top >= 0 && bounds.Right <= width && bounds.Bottom <= height,
                $"inspector escapes {width}x{height}");
            int count = live::AICompanion.Companion.Brain.Infrastructure.Diagnostics.BrainOverlay.VisibleRows(bounds);
            for (int row = 0; row < count; row++)
            {
                var item = live::AICompanion.Companion.Brain.Infrastructure.Diagnostics.BrainOverlay.RowBounds(bounds, row);
                Require(bounds.Contains(item) && item.Bottom <= bounds.Bottom - 20,
                    "scrollable inspector row overlaps its footer or escapes its panel");
            }
        }
    }

    internal static void Attach(BrainTelemetry recorder)
    {
        var mod = ModContent.GetInstance<live::AICompanion.AICompanion>() ?? new live::AICompanion.AICompanion();
        typeof(Mod).GetProperty("Logger")!.SetValue(mod, log4net.LogManager.GetLogger(typeof(VerifyObservationLifecycle)));
        if (ModContent.GetInstance<live::AICompanion.AICompanion>() == null) ContentInstance.Register(mod);
        if (ModContent.GetInstance<BrainTelemetry>() == null) ContentInstance.Register(recorder);
        typeof(ModType).GetProperty("Mod")!.SetValue(recorder, mod);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
