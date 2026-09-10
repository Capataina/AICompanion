extern alias live;

using System.Reflection;
using Terraria;
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

    private static void Attach(BrainTelemetry recorder)
    {
        var mod = new live::AICompanion.AICompanion();
        typeof(Mod).GetProperty("Logger")!.SetValue(mod, log4net.LogManager.GetLogger(typeof(VerifyObservationLifecycle)));
        ContentInstance.Register(mod);
        typeof(ModType).GetProperty("Mod")!.SetValue(recorder, mod);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
