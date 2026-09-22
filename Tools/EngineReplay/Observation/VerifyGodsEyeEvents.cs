#nullable enable

using System.Text.Json;
using AICompanion.Companion.Brain.Infrastructure.Diagnostics;
using AICompanion.Companion.Brain.Infrastructure.Movement;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.DataStructures;

/// <summary>Runs the real sparse-event writer and native hook instances against EngineReplay's map.</summary>
internal static class VerifyGodsEyeEvents
{
    public static int Run()
    {
        string stem = Path.Combine(Path.GetTempPath(), $"aic-gods-eye-{Guid.NewGuid():N}");
        string path = stem + ".jsonl", tsv = stem + ".tsv";
        try
        {
            File.WriteAllText(tsv, "");
            using FlushDiagnosticRecords writer = FlushDiagnosticRecords.Start(tsv, path);
            return Verify(path, writer);
        }
        finally
        {
            GodsEyeEvents.Close();
            if (File.Exists(path)) File.Delete(path);
            if (File.Exists(tsv)) File.Delete(tsv);
        }
    }

    private static int Verify(string path, FlushDiagnosticRecords writer)
    {
        MovementQueries.World = new GameTileWorld();
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
        var toolBefore = new AICompanion.Companion.Brain.Infrastructure.Interactions.TileToolState(true, 7, 18, 36, 30);
        var toolAfter = toolBefore with { Damage = 65 };
        var toolOutcome = new AICompanion.Companion.Brain.Infrastructure.Interactions.TileToolObservation(
            Main.GameUpdateCount, 3, new Point(25, 59), Terraria.ID.ItemID.CopperPickaxe, toolBefore, toolAfter);
        GodsEyeEvents.RecordToolEffect(reusedNpc, "pickaxe", toolOutcome, 12, 34, 56);
        GodsEyeEvents.RecordToolEffect(reusedNpc, "pickaxe", toolOutcome with
        {
            Attempt = 4, Before = toolAfter, After = toolAfter,
        }, 12, 34, 0);

        Projectile first = Projectile(9, new Vector2(320f, 800f), new Vector2(10f, 0f));
        projectileHooks.OnSpawn(first, source);
        GodsEyeEvents.RecordShot(reusedNpc, target, first.whoAmI, first.Center, first.velocity, new Vector2(500f, 800f), "fixture");
        Vector2 firstSpawn = first.Center;
        first.position += new Vector2(48f, 16f);
        projectileHooks.OnTileCollide(first, first.velocity);
        Projectile reusedProjectile = Projectile(9, new Vector2(800f, 800f), new Vector2(-4f, 0f));
        projectileHooks.OnSpawn(reusedProjectile, source);

        Player owner = new() { width = 20, height = 42, position = new Vector2(320f, 800f), active = true };
        var playerHooks = new AICompanion.Companion.PlayerIntegration.CompanionPlayer().NewInstance(owner);
        owner.statLife = 100;
        playerHooks.OnHurt(new Player.HurtInfo { Damage = 20 });
        owner.statLife = 10;
        playerHooks.OnHurt(new Player.HurtInfo { Damage = 300 });
        // One item slot, three occupants, and the two ways a slot changes hands. Generations used to
        // advance only from `GlobalItem.OnSpawn`, which a drop staged straight into `Main.item` never
        // fires, so slot 3 carried generation 1 through all three of these and `pickup.related` joined
        // a transfer to whichever a reader assumed. Nothing here calls the spawn hook, deliberately:
        // this is exactly the path that produced the defect.
        Item dropped = Drop(3, type: 71, stack: 12, new Vector2(600f, 800f), age: 5);
        GodsEyeEvents.RecordDropSighted(dropped, 40f, 0.25f);
        int firstDrop = GodsEyeEvents.ItemIdentity(dropped);
        dropped.timeSinceItemSpawned = 40;
        int sameDrop = GodsEyeEvents.ItemIdentity(dropped);
        // A different type in the same slot.
        Item second = Drop(3, type: 73, stack: 1, new Vector2(640f, 800f), age: 0);
        int secondDrop = GodsEyeEvents.ItemIdentity(second);
        second.timeSinceItemSpawned = 90;
        GodsEyeEvents.RecordPickup(reusedNpc, second, 1, "cargo", 0);
        // The same type again in the same slot, which the type alone cannot separate: the only thing
        // that says these are two items is the spawn age going backwards, from the 90 the slot's last
        // occupant had reached to the 1 a fresh drop carries.
        Item third = Drop(3, type: 73, stack: 4, new Vector2(660f, 800f), age: 1);
        int thirdDrop = GodsEyeEvents.ItemIdentity(third);

        RecordTerrainChunks.ObserveActors(reusedNpc, owner);
        var capture = new RecordTerrainChunks();
        for (int tick = 0; tick < 49; tick++) capture.PostUpdateEverything();
        writer.FlushForReader(TimeSpan.FromSeconds(1));
        int beforeEdit = Read(path).Count(record => record.Kind == "terrain-snapshot");
        terrainHooks.PlaceInWorld(21, 50, 1, new Item());
        writer.FlushForReader(TimeSpan.FromSeconds(1));
        int beforePostUpdate = Read(path).Count(record => record.Kind == "terrain-snapshot");
        Tile changed = Main.tile[21, 50];
        changed.HasTile = true;
        changed.TileType = 1;
        changed.Slope = Terraria.ID.SlopeType.SlopeDownLeft;
        changed.LiquidAmount = 255;
        changed.LiquidType = 0;
        changed.TileFrameX = 36; changed.TileFrameY = 54;
        capture.PostUpdateEverything();
        GodsEyeEvents.Close();
        writer.Stop(TimeSpan.FromSeconds(1), "fixture-complete");

        List<Event> events = Read(path);
        int failures = 0;
        Event[] toolEvents = events.Where(record => record.Kind == "tool-effect").ToArray();
        failures += Require(toolEvents.Length == 2 && toolEvents[0].Subject == 21_000_002
            && toolEvents[0].Channel == "attempt=3;choice-id=12;activity-id=34;activity-attempt-id=56" && toolEvents[1].Channel == "attempt=4;choice-id=12;activity-id=34;activity-attempt-id=0"
            && toolEvents[0].Detail.Contains("effect=Damaged;before-present=True;before-type=7;before-frame=18,36;before-damage=30")
            && toolEvents[0].Detail.Contains("after-damage=65;damage-scope=tool-owned-hit-table;yield=unobserved")
            && toolEvents[1].Detail.Contains("effect=NoObservedChange"),
            "tool events must retain actor generation, attempt identity, native snapshots and limits on yield attribution");
        Event[] playerHits = events.Where(record => record.Kind == "player-damage").ToArray();
        failures += Require(playerHits.Length == 2
            && playerHits[0].Channel == "before-health-subtraction"
            && playerHits[0].Detail.Contains("life-before=100;expected-life-after=80")
            && playerHits[1].Detail.Contains("life-before=10;expected-life-after=0"),
            "OnHurt must record actual pre-hit health and label its predicted successor, including fatal overkill");
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

        failures += Require(sameDrop == firstDrop,
            $"one item aging in its slot must keep its identity; {firstDrop} became {sameDrop}");
        failures += Require(secondDrop != firstDrop,
            $"a different item type in a reused slot must open a new generation; both read {secondDrop}");
        failures += Require(thirdDrop != secondDrop,
            $"the same type in a reused slot must be told apart by its spawn age going backwards; both read {thirdDrop}");
        Event sighted = events.Single(record => record.Kind == "drop-sighted");
        failures += Require(sighted.Related == firstDrop.ToString()
                && sighted.Channel == "loot-sense" && sighted.Position == dropped.Center
                && sighted.Detail.Contains("slot=3;stack=12;distance=40.0"),
            $"a sighted drop must name its identity, slot, stack and where it lay; related={sighted.Related}, detail={sighted.Detail}");
        Event pickup = events.Single(record => record.Kind == "pickup");
        failures += Require(pickup.Related == secondDrop.ToString(),
            $"a pickup must join the item actually taken rather than the slot's first ever occupant;"
                + $" related={pickup.Related}, taken={secondDrop}, first={firstDrop}");

        Event? changedSnapshot = events.Where(record => record.Kind == "terrain-snapshot" && record.Position == new Vector2(256f, 768f)).Cast<Event?>().LastOrDefault();
        bool terrainComplete = changedSnapshot is { Detail: string detail }
            && detail.Contains("width=16;height=16;")
            && detail.Contains("liquid-amount-type=")
            && detail.Contains("tile-wall-u16le=")
            && detail.Contains("tile-state-u8=")
            && detail.Contains("tile-frame-i16le=")
            && !detail.Contains("tiles=" + new string('?', 256));
        failures += Require(terrainComplete, "post-update terrain snapshot omitted tile shape, liquid/material state, or local world data");
        if (changedSnapshot is { } snapshot)
        {
            string Field(string name) => snapshot.Detail.Split(';').Single(p => p.StartsWith(name + "="))[(name.Length + 1)..];
            byte[] state = Convert.FromBase64String(Field("tile-state-u8"));
            byte[] frame = Convert.FromBase64String(Field("tile-frame-i16le"));
            int cell = (50 - 48) * 16 + (21 - 16);
            failures += Require((state[cell] & 1) == 1 && ((state[cell] >> 4) & 7) == (int)changed.Slope
                && frame[cell * 4] == 36 && frame[cell * 4 + 2] == 54,
                "terrain state/frame bytes cannot reconstruct the edited slope and native frame");
        }

        Console.WriteLine($"GodsEye events: {(failures == 0 ? "all checks passed" : $"{failures} failures")}; {events.Count} real writer records, native hooks, generation links and terrain timing exercised");
        return failures;
    }

    private static Event Single(IReadOnlyList<Event> events, string kind, int subject, ref int failures)
    {
        Event? match = events.SingleOrDefault(record => record.Kind == kind && record.Subject == subject);
        if (match != null) return match.Value;
        failures++;
        AICompanion.Tools.Ledger.EmitLedgerRows.Detail($"GodsEye events: missing {kind} for {subject}");
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
                root.GetProperty("kind").GetString()!, root.GetProperty("subject").GetInt32(),
                root.GetProperty("related").GetString()!, root.GetProperty("channel").GetString()!,
                new Vector2(root.GetProperty("pos_x").GetSingle(), root.GetProperty("pos_y").GetSingle()), root.GetProperty("detail").GetString()!));
        }
        return events;
    }

    private static NPC Npc(int slot, Vector2 position, Vector2 velocity)
        => new() { whoAmI = slot, type = slot, active = true, width = 16, height = 32, position = position, velocity = velocity, life = 100 };

    /// <summary>A world drop staged straight into its slot, which is the path that never fires
    /// <c>GlobalItem.OnSpawn</c> and therefore the path the identity fix had to be built against.
    /// No <c>SetDefaults</c>: the identity reads the type, the stack and the spawn age, and loading an
    /// item's real definition here would make the row depend on the game's content tables.</summary>
    private static Item Drop(int slot, int type, int stack, Vector2 position, int age)
        => new() { whoAmI = slot, type = type, stack = stack, active = true, width = 16, height = 16,
            position = position, timeSinceItemSpawned = age };

    private static Projectile Projectile(int slot, Vector2 position, Vector2 velocity)
        => new() { whoAmI = slot, type = 1, active = true, width = 10, height = 10, position = position, velocity = velocity, damage = 20, owner = Main.myPlayer };

    private static int Require(bool condition, string detail)
    {
        if (condition) return 0;
        AICompanion.Tools.Ledger.EmitLedgerRows.Detail("GodsEye events: " + detail);
        return 1;
    }

    private readonly record struct Event(int Version, int Sequence, double Elapsed, string Kind, int Subject, string Related, string Channel, Vector2 Position, string Detail);
}
