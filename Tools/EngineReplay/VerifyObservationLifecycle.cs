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
        companion.AI(); BrainTelemetry.Record(companion); recorder.OnWorldUnload();
        string[] lines = File.ReadAllLines(path);
        int header = Array.FindIndex(lines, l => l.StartsWith("tick\t"));
        Require(header >= 0 && header + 1 < lines.Length, "real sample writer emitted no table row");
        string[] names = lines[header].Split('\t'), values = lines[header + 1].Split('\t');
        Require(names.Length == values.Length, $"sample/header widths disagree: {names.Length}/{values.Length}");
        foreach (string name in new[] { "escape_stage", "state_search_pending", "head_submerged", "attack_value", "hunt_reason", "nav_status" })
            Require(Array.IndexOf(names, name) >= 0, "causal sample field missing: " + name);
        Require(lines.Any(l => l.StartsWith("# text_columns=")), "writer must declare its textual columns");
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
