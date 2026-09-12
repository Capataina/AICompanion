extern alias live;

using System.Reflection;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;
using BrainTelemetry = live::AICompanion.Companion.Brain.BehaviourDiagnostics.BrainTelemetry;

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
        string root = Path.Combine(Path.GetTempPath(), "aic-observation-lifecycle-" + Guid.NewGuid().ToString("N"));
        savePath.SetValue(null, root);
        try
        {
            VerifySameStemGainsAnAttemptSuffix();
            VerifyZeroTickLifecycleMetadata();
            VerifyRecordingSwitch();
            VerifyInspectorGeometry();
            VerifyNotchOpeningConsumesThePress();
            VerifyOneCompleteSample();
            VerifyRecoveryDoesNotRefreshTheChoice();
            Console.WriteLine("observation lifecycle: reserved retry names, zero-tick metadata and callback-scoped lifecycle evidence passed");
            return 0;
        }
        catch (Exception error)
        {
            Console.WriteLine("FAIL observation lifecycle: " + error);
            return 1;
        }
        finally
        {
            Close();
            savePath.SetValue(null, priorSavePath);
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static void VerifyOneCompleteSample()
    {
        var recorder = new BrainTelemetry(); Attach(recorder);
        var companion = VerifyCompanionLifecycle.Create();
        recorder.OnWorldLoad();
        string path = Directory.GetFiles(BrainTelemetry.Folder, "*.tsv").OrderByDescending(File.GetLastWriteTimeUtc).First();
        VerifyObservedMotion.SetTick(Main.GameUpdateCount + 1);
        companion.AI(); recorder.OnWorldUnload();
        string[] lines = File.ReadAllLines(path);
        int header = Array.FindIndex(lines, l => l.StartsWith("tick\t"));
        Require(header >= 0 && header + 1 < lines.Length, "real sample writer emitted no table row");
        string[] names = lines[header].Split('\t'), values = lines[header + 1].Split('\t');
        Require(names.Length == values.Length, $"sample/header widths disagree: {names.Length}/{values.Length}");
        foreach (string name in new[] { "escape_stage", "state_search_pending", "head_submerged", "attack_value", "hunt_reason", "nav_status" })
            Require(Array.IndexOf(names, name) >= 0, "causal sample field missing: " + name);
        Require(lines.Any(l => l.StartsWith("# text_columns=")), "writer must declare its textual columns");
        string events = File.ReadAllText(Path.ChangeExtension(path, null) + "-events.jsonl");
        foreach (string family in new[] { "Gathering", "Combat", "NearbyAssistance" })
            Require(events.Contains("family:" + family + "=child:"), "decision writer omitted a family nomination: " + family);
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
        companion.AI();
        // Start from an already active recovery so the actual coordinator takes its early
        // return before choosing. The owner stays inside the native fixture's world.
        Main.LocalPlayer.dead = false;
        Main.LocalPlayer.position = new Microsoft.Xna.Framework.Vector2(1100, 1398);
        typeof(live::AICompanion.Companion.Brain.Behaviours.Companionship.RecoverDistantFollowing)
            .GetField("<Active>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(companion.Brain.FollowRecovery, true);
        VerifyObservedMotion.SetTick((Main.GameUpdateCount / 60 + 1) * 60);
        companion.AI();
        Require(companion.Motor.ControlSource == "follow-recovery-flight", "fixture did not enter the actual recovery control path");
        Main.LocalPlayer.dead = true;
        VerifyObservedMotion.SetTick(Main.GameUpdateCount + 1);
        companion.AI();
        Require(companion.Brain.Chooser.Current == null, "the empty post-recovery board must have no ordinary activity");
        Main.LocalPlayer.dead = false;
        Main.LocalPlayer.Bottom = companion.NPC.Bottom;
        VerifyObservedMotion.SetTick(Main.GameUpdateCount + 1);
        companion.AI();
        companion.CheckDead();
        VerifyObservedMotion.SetTick(Main.GameUpdateCount + 1);
        companion.AI();
        recorder.OnWorldUnload();
        string events = Path.ChangeExtension(path, null) + "-events.jsonl";
        string[] activities = File.ReadLines(events).Where(line => line.Contains("\"kind\":\"activity-state\"", StringComparison.Ordinal)).ToArray();
        Require(activities.Length == 5 && activities[0].Contains("phase=Executing")
            && activities[1].Contains("phase=Suspended;reason=follow-recovery-flight")
            && activities[2].Contains("phase=None") && activities[3].Contains("phase=Executing")
            && activities[4].Contains("phase=Suspended;reason=downed"),
            "the real recorder must distinguish execution, recovery suspension, no offer, resumed activity and downing; actual records: "
                + string.Join("\n", activities));
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
        typeof(BrainTelemetry).GetMethod("Close", BindingFlags.Static | BindingFlags.NonPublic)?.Invoke(null, null);
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
            Main.mouseX = box.Center.X; Main.mouseY = box.Center.Y; Main.mouseLeft = true;
            Main.LocalPlayer.mouseInterface = false;
            // No Draw has run: the cursor entered and pressed during this very update.
            owner.PreUpdate();
            Require(Main.LocalPlayer.mouseInterface, "the opening notch press must be consumed before the first interface draw");
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
        live::AICompanion.Companion.Brain.WorldObservation.PredictObservedMotion.Observe(subject);
        var forecast = live::AICompanion.Companion.Brain.WorldObservation.PredictObservedMotion.ExistingForecast(subject);
        Require(forecast.Count > 0, "fixture must contain an active gameplay forecast");
        config.RecordTelemetry = false; config.OnChanged();
        Require(ReferenceEquals(forecast, live::AICompanion.Companion.Brain.WorldObservation.PredictObservedMotion.ExistingForecast(subject)),
            "turning off telemetry must preserve gameplay prediction state");
        using (File.Open(newest, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
        long bytes = new FileInfo(newest).Length;
        recorder.PostUpdateEverything();
        Require(new FileInfo(newest).Length == bytes, "disabled capture must not append samples");
        config.RecordTelemetry = true;
        config.EnableBrainInspector = false; config.OnChanged();
        Require(!live::AICompanion.Companion.Brain.BehaviourDiagnostics.BrainOverlay.MayCapture,
            "disabled inspector must not retain simulation traces");
        config.EnableBrainInspector = true;
    }

    private static void VerifyInspectorGeometry()
    {
        foreach (var viewport in new[] { (640, 360), (800, 600), (1280, 720), (1920, 1080) })
        foreach (float scale in new[] { 1f, 1.25f, 1.5f })
        {
            int width = (int)(viewport.Item1 / scale), height = (int)(viewport.Item2 / scale);
            var bounds = live::AICompanion.Companion.Brain.BehaviourDiagnostics.BrainOverlay.PanelBounds(width, height);
            Require(bounds.Left >= 0 && bounds.Top >= 0 && bounds.Right <= width && bounds.Bottom <= height,
                $"inspector escapes {width}x{height}");
            int count = live::AICompanion.Companion.Brain.BehaviourDiagnostics.BrainOverlay.VisibleRows(bounds);
            for (int row = 0; row < count; row++)
            {
                var item = live::AICompanion.Companion.Brain.BehaviourDiagnostics.BrainOverlay.RowBounds(bounds, row);
                Require(bounds.Contains(item) && item.Bottom <= bounds.Bottom - 20,
                    "scrollable inspector row overlaps its footer or escapes its panel");
            }
        }
    }

    private static void Attach(BrainTelemetry recorder)
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
