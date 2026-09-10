extern alias live;

using Microsoft.Xna.Framework;
using Terraria.ModLoader.IO;

internal static class VerifyRoutePersistence
{
    public static int Run()
    {
        var owner = new live::AICompanion.Companion.Brain.SharedMovementSystem.ResetTerrainChanges();
        // Native ModSystem instances are loader-attached. Keep that contract in this fixture so
        // invalid optional data exercises the real warning path instead of a null test owner.
        var mod = new live::AICompanion.AICompanion();
        typeof(Terraria.ModLoader.Mod).GetProperty("Logger")!.SetValue(mod, log4net.LogManager.GetLogger(typeof(VerifyRoutePersistence)));
        typeof(Terraria.ModLoader.ModType).GetProperty("Mod")!.SetValue(owner, mod);
        var memory = live::AICompanion.Companion.Brain.SharedMovementSystem.RememberExecutedRoutes.World;
        owner.OnWorldLoad();
        try
        {
            FillToCapacity(memory);
            Require(memory.Count == 1024, "full-capacity route fixture must contain every recorded edge");

            var tag = new TagCompound();
            owner.SaveWorldData(tag);
            Require(tag.TryGet("executedRoutes", out byte[]? archive) && archive != null,
                "route memory must save as a byte array");
            byte[] archiveBytes = archive!;
            Require(archiveBytes.Length > short.MaxValue,
                "full-capacity archive must exercise the former signed-short string boundary");

            string path = Path.Combine(Path.GetTempPath(), $"aic-route-memory-{Guid.NewGuid():N}.twld");
            try
            {
                TagIO.ToFile(tag, path, compress: true);
                TagCompound decodedTag = TagIO.FromFile(path, compressed: true);
                Require(decodedTag.TryGet("executedRoutes", out byte[]? decoded) && decoded != null
                    && archiveBytes.SequenceEqual(decoded), "compressed TagIO round trip must preserve the full byte archive");
                owner.OnWorldLoad();
                owner.LoadWorldData(decodedTag);
                Require(memory.Count == 1024, "full byte archive must restore every route edge");
            }
            finally
            {
                File.Delete(path);
            }

            owner.LoadWorldData(new TagCompound());
            Require(memory.Count == 0, "a world without route memory must not inherit static route state");

            RecordOne(memory);
            var legacy = new TagCompound { ["executedRoutes"] = memory.Save() };
            string legacyPath = Path.Combine(Path.GetTempPath(), $"aic-route-legacy-{Guid.NewGuid():N}.twld");
            try
            {
                TagIO.ToFile(legacy, legacyPath, compress: true);
                owner.OnWorldLoad();
                owner.LoadWorldData(TagIO.FromFile(legacyPath, compressed: true));
                Require(memory.Count == 1, "a small legacy string archive must remain readable");
            }
            finally
            {
                File.Delete(legacyPath);
            }

            AssertDiscarded(owner, memory, new byte[] { 0xc3, 0x28 }, "malformed UTF-8");
            AssertDiscarded(owner, memory, new byte[1024 * 1024 + 1], "oversized byte archive");
            AssertDiscarded(owner, memory, System.Text.Encoding.UTF8.GetBytes("{broken"), "malformed JSON");
            Console.WriteLine("route persistence: byte archives survive compressed TagIO at full capacity; legacy and invalid optional data are handled safely");
            return 0;
        }
        finally
        {
            owner.OnWorldUnload();
        }
    }

    private static void FillToCapacity(live::AICompanion.Companion.Brain.SharedMovementSystem.RememberExecutedRoutes memory)
    {
        memory.Clear();
        for (int i = 0; i < 1024; i++) Record(memory, i);
    }

    private static void RecordOne(live::AICompanion.Companion.Brain.SharedMovementSystem.RememberExecutedRoutes memory)
    {
        memory.Clear();
        Record(memory, 0);
    }

    private static void Record(live::AICompanion.Companion.Brain.SharedMovementSystem.RememberExecutedRoutes memory, int index)
    {
        int left = (index + 10) * 16;
        var entry = new live::AICompanion.Companion.Brain.SharedMovementSystem.BodyState(left, 176, 0, 0, true,
            Capabilities: live::AICompanion.Companion.Brain.SharedMovementSystem.MovementCapabilities.Basic);
        var end = entry with { Left = left + 16 };
        var step = new live::AICompanion.Companion.Brain.SharedMovementSystem.NavStep(end.FeetTile,
            live::AICompanion.Companion.Brain.SharedMovementSystem.MoveKind.Walk, entry.FeetTile, Ticks: 10);
        memory.Record(new EmptyWorld(), step, entry, end, new Rectangle(left, 134, 36, 42));
    }

    private static void AssertDiscarded(live::AICompanion.Companion.Brain.SharedMovementSystem.ResetTerrainChanges owner,
        live::AICompanion.Companion.Brain.SharedMovementSystem.RememberExecutedRoutes memory, byte[] archive, string description)
    {
        RecordOne(memory);
        owner.LoadWorldData(new TagCompound { ["executedRoutes"] = archive });
        Require(memory.Count == 0, $"{description} must be discarded without retaining old route state");
    }

    private sealed class EmptyWorld : live::AICompanion.Companion.Brain.SharedMovementSystem.ITileWorld
    {
        public bool InWorld(int x, int y) => true;
        public live::AICompanion.Companion.Brain.SharedMovementSystem.TileShape Shape(int x, int y)
            => live::AICompanion.Companion.Brain.SharedMovementSystem.TileShape.Air;
        public bool PassThrough(int x, int y) => false;
        public bool Water(int x, int y) => false;
        public bool Lava(int x, int y) => false;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
