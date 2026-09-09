#nullable enable

using System.Text.Json;
using AICompanion.Companion.Brain.BehaviourDiagnostics;
using AICompanion.Companion.Brain.SharedMovementSystem;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.DataStructures;

/// <summary>Runs the real sparse-event writer and native hook instances against EngineReplay's map.</summary>
internal static class VerifyGodsEyeEvents
{
    public static int Run()
    {
        string path = Path.Combine(Path.GetTempPath(), $"aic-gods-eye-{Guid.NewGuid():N}.jsonl");
        try
        {
            return Verify(path);
        }
        finally
        {
            GodsEyeEvents.Close();
            if (File.Exists(path)) File.Delete(path);
        }
    }

    private static int Verify(string path)
    {
        NavGrid.World = new GameTileWorld();
        GodsEyeEvents.Open(path);
        var source = new EntitySource_Misc("engine-replay");
        var npcHooks = new ObserveNativeNpcEvents();
        var projectileHooks = new ObserveNativeProjectileEvents();
        var terrainHooks = new ObserveTerrainChanges();

        NPC shooter = Npc(21, new Vector2(320f, 800f), new Vector2(2f, -1f));
        NPC target = Npc(22, new Vector2(400f, 800f), Vector2.Zero);
        npcHooks.OnSpawn(shooter, source);
        npcHooks.OnSpawn(target, source);
        Vector2 shooterSpawn = shooter.Center;
        shooter.position += new Vector2(80f, 0f);
        npcHooks.OnKill(shooter);
        NPC reusedNpc = Npc(21, new Vector2(720f, 800f), Vector2.Zero);
        npcHooks.OnSpawn(reusedNpc, source);

        Projectile first = Projectile(9, new Vector2(320f, 800f), new Vector2(10f, 0f));
        projectileHooks.OnSpawn(first, source);
        GodsEyeEvents.RecordShot(reusedNpc, target, first.whoAmI, first.Center, first.velocity, new Vector2(500f, 800f), "fixture");
        Vector2 firstSpawn = first.Center;
        first.position += new Vector2(48f, 16f);
        projectileHooks.OnTileCollide(first, first.velocity);
        Projectile reusedProjectile = Projectile(9, new Vector2(800f, 800f), new Vector2(-4f, 0f));
        projectileHooks.OnSpawn(reusedProjectile, source);

        Player owner = new() { width = 20, height = 42, position = new Vector2(320f, 800f), active = true };
        RecordTerrainChunks.ObserveActors(reusedNpc, owner);
        var capture = new RecordTerrainChunks();
        for (int tick = 0; tick < 49; tick++) capture.PostUpdateEverything();
        GodsEyeEvents.Flush();
        int beforeEdit = Read(path).Count(record => record.Kind == "terrain-snapshot");
        terrainHooks.PlaceInWorld(21, 50, 1, new Item());
        GodsEyeEvents.Flush();
        int beforePostUpdate = Read(path).Count(record => record.Kind == "terrain-snapshot");
        Tile changed = Main.tile[21, 50];
        changed.HasTile = true;
        changed.TileType = 1;
        changed.Slope = Terraria.ID.SlopeType.SlopeDownLeft;
        changed.LiquidAmount = 255;
        changed.LiquidType = 0;
        capture.PostUpdateEverything();
        GodsEyeEvents.Close();

        List<Event> events = Read(path);
        int failures = 0;
        failures += Require(beforeEdit > 0, "the initial rolling terrain scan wrote no event");
        failures += Require(beforePostUpdate == beforeEdit, "a dirty tile was recorded before post-update observed the applied edit");
        failures += Require(events[^1].Kind == "session-end", "normal close did not write session-end");
        failures += Require(events.All(record => record.Version == 1 && double.IsFinite(record.Elapsed) && record.Sequence >= 0), "event schema, timestamp, or sequence was invalid");
        failures += Require(events.Select(record => record.Sequence).SequenceEqual(Enumerable.Range(0, events.Count)), "event sequence was not contiguous");

        Event projectileSpawn = Single(events, "projectile-spawn", first.whoAmI * 1_000_000 + 1, ref failures);
        Event terrainHit = Single(events, "projectile-terrain-hit", first.whoAmI * 1_000_000 + 1, ref failures);
        Event shot = events.SingleOrDefault(record => record.Kind == "shot");
        Event reusedProjectileSpawn = Single(events, "projectile-spawn", reusedProjectile.whoAmI * 1_000_000 + 2, ref failures);
        failures += Require(shot.Channel == $"projectile={first.whoAmI * 1_000_000 + 1}" && terrainHit.Subject == projectileSpawn.Subject,
            "shot and terrain contact did not share the first projectile generation");
        failures += Require(reusedProjectileSpawn.Subject != projectileSpawn.Subject, "reused projectile slot retained its old generation");
        failures += Require(projectileSpawn.Position == firstSpawn && terrainHit.Position != projectileSpawn.Position,
            "projectile events did not snapshot state at occurrence time");

        Event spawn = Single(events, "npc-spawn", 21_000_001, ref failures);
        Event death = Single(events, "npc-death", 21_000_001, ref failures);
        Event respawn = Single(events, "npc-spawn", 21_000_002, ref failures);
        failures += Require(spawn.Position == shooterSpawn && death.Position != spawn.Position && respawn.Subject != spawn.Subject,
            "NPC spawn/death or slot reuse generation did not retain occurrence state");

        Event? changedSnapshot = events.Where(record => record.Kind == "terrain-snapshot" && record.Position == new Vector2(256f, 768f)).Cast<Event?>().LastOrDefault();
        bool terrainComplete = changedSnapshot is { Detail: string detail }
            && detail.Contains("width=16;height=16;")
            && detail.Contains("liquid-amount-type=")
            && detail.Contains("tile-wall-u16le=")
            && !detail.Contains("tiles=" + new string('?', 256));
        failures += Require(terrainComplete, "post-update terrain snapshot omitted tile shape, liquid/material state, or local world data");

        Console.WriteLine($"GodsEye events: {(failures == 0 ? "all checks passed" : $"{failures} failures")}; {events.Count} real writer records, native hooks, generation links and terrain timing exercised");
        return failures;
    }

    private static Event Single(IReadOnlyList<Event> events, string kind, int subject, ref int failures)
    {
        Event? match = events.SingleOrDefault(record => record.Kind == kind && record.Subject == subject);
        if (match != null) return match.Value;
        failures++;
        Console.WriteLine($"FAIL GodsEye events: missing {kind} for {subject}");
        return default;
    }

    private static List<Event> Read(string path)
    {
        var events = new List<Event>();
        foreach (string line in File.ReadLines(path))
        {
            using JsonDocument document = JsonDocument.Parse(line);
            JsonElement root = document.RootElement;
            events.Add(new Event(
                root.GetProperty("v").GetInt32(), root.GetProperty("seq").GetInt32(), root.GetProperty("wall_elapsed_ms").GetDouble(),
                root.GetProperty("kind").GetString()!, root.GetProperty("subject").GetInt32(), root.GetProperty("channel").GetString()!,
                new Vector2(root.GetProperty("pos_x").GetSingle(), root.GetProperty("pos_y").GetSingle()), root.GetProperty("detail").GetString()!));
        }
        return events;
    }

    private static NPC Npc(int slot, Vector2 position, Vector2 velocity)
        => new() { whoAmI = slot, type = slot, active = true, width = 16, height = 32, position = position, velocity = velocity, life = 100 };

    private static Projectile Projectile(int slot, Vector2 position, Vector2 velocity)
        => new() { whoAmI = slot, type = 1, active = true, width = 10, height = 10, position = position, velocity = velocity, damage = 20, owner = Main.myPlayer };

    private static int Require(bool condition, string detail)
    {
        if (condition) return 0;
        Console.WriteLine("FAIL GodsEye events: " + detail);
        return 1;
    }

    private readonly record struct Event(int Version, int Sequence, double Elapsed, string Kind, int Subject, string Channel, Vector2 Position, string Detail);
}
