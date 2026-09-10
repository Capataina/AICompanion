extern alias live;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using Microsoft.Xna.Framework;
using Terraria;
using AICompanion.Companion.Brain.SharedMovementSystem;

/// <summary>Replays one recorded submerged pose against locally captured terrain. Unknown
/// tiles are closed boundaries; reaching them is reduced coverage, never proof of no route.</summary>
internal static class ReplayRecordedWater
{
    public static int Run(string path, int tick)
    {
        var lines = File.ReadLines(path).Where(l => l.Length > 0 && !l.StartsWith('#')).GetEnumerator();
        if (!lines.MoveNext()) throw new InvalidDataException("Capture has no TSV header");
        string[] header = lines.Current.TrimStart('\uFEFF').Split('\t');
        Dictionary<string, string>? row = null;
        while (lines.MoveNext())
        {
            string[] cells = lines.Current.Split('\t');
            if (cells.Length != header.Length) continue;
            var candidate = header.Zip(cells).ToDictionary(p => p.First, p => p.Second);
            if (int.Parse(candidate["tick"], CultureInfo.InvariantCulture) == tick) { row = candidate; break; }
        }
        lines.Dispose();
        if (row == null) throw new InvalidDataException($"No complete sample at tick {tick}");
        float Number(string key) => float.Parse(row[key], CultureInfo.InvariantCulture);
        Vector2 Pair(string key) { string[] p = row[key].Split(','); return new(float.Parse(p[0], CultureInfo.InvariantCulture), float.Parse(p[1], CultureInfo.InvariantCulture)); }
        Vector2 original = new(Number("observed_left"), Number("observed_bottom"));
        double sampleElapsed = double.Parse(row["wall_elapsed_ms"], CultureInfo.InvariantCulture);
        const int side = 160;
        int ox = (int)(original.X / 16) - side / 2, oy = (int)(original.Y / 16) - side / 2;
        Vector2 offset = new(ox * 16, oy * 16);
        typeof(Terraria.Program).GetField("SavePath", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!.SetValue(null, Path.GetTempPath());
        Main.dedServ = true; Main.maxTilesX = Main.maxTilesY = side; Main.worldSurface = 0;
        Main.tile = (Tilemap)Activator.CreateInstance(typeof(Tilemap), BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public, null, new object[] { (ushort)side, (ushort)side }, null)!;
        Main.tileSolid[1] = true; Main.tileSolid[19] = Main.tileSolidTop[19] = true;
        var known = new bool[side, side];
        for (int x = 0; x < side; x++) for (int y = 0; y < side; y++)
        { Tile tile = Main.tile[x, y]; tile.HasTile = true; tile.TileType = 1; }
        int snapshots = 0, legacySnapshots = 0; long oldest = long.MaxValue, newest = 0;
        string events = Path.ChangeExtension(path, null) + "-events.jsonl";
        foreach (string line in File.ReadLines(events))
        {
            using JsonDocument doc = JsonDocument.Parse(line);
            JsonElement e = doc.RootElement;
            if (e.GetProperty("kind").GetString() != "terrain-snapshot" || e.GetProperty("wall_elapsed_ms").GetDouble() > sampleElapsed) continue;
            int cx = (int)(e.GetProperty("pos_x").GetSingle() / 16) - ox, cy = (int)(e.GetProperty("pos_y").GetSingle() / 16) - oy;
            if (cx < -16 || cy < -16 || cx >= side || cy >= side) continue;
            var fields = e.GetProperty("detail").GetString()!.Split(';').Select(p => p.Split('=', 2)).Where(p => p.Length == 2).ToDictionary(p => p[0], p => p[1]);
            if (!fields.TryGetValue("liquid-amount-type", out string? liquidData)) throw new InvalidDataException("Terrain capture lacks exact liquid amounts");
            int w = int.Parse(fields["width"]), h = int.Parse(fields["height"]);
            string glyphs = fields["tiles"]; byte[] liquids = Convert.FromBase64String(liquidData);
            byte[]? states = fields.TryGetValue("tile-state-u8", out string? stateData) ? Convert.FromBase64String(stateData) : null;
            byte[]? materials = states == null ? null : Convert.FromBase64String(fields["tile-wall-u16le"]);
            byte[]? frames = states == null ? null : Convert.FromBase64String(fields["tile-frame-i16le"]);
            if (glyphs.Length != w * h || liquids.Length != w * h * 2) throw new InvalidDataException("Terrain snapshot dimensions disagree with its payload");
            if (states != null && (states.Length != w * h || materials!.Length != w * h * 4 || frames!.Length != w * h * 4))
                throw new InvalidDataException("Terrain material payload dimensions disagree");
            if (states == null) legacySnapshots++;
            snapshots++; oldest = Math.Min(oldest, e.GetProperty("tick").GetInt64()); newest = Math.Max(newest, e.GetProperty("tick").GetInt64());
            for (int i = 0; i < glyphs.Length; i++)
            {
                int x = cx + i % w, y = cy + i / w;
                if (x < 0 || y < 0 || x >= side || y >= side || glyphs[i] == '?') continue;
                known[x, y] = true;
                Tile tile = Main.tile[x, y]; tile.ClearEverything();
                char g = glyphs[i]; TileShape shape = TextTileWorld.ShapeOf(g);
                tile.HasTile = shape != TileShape.Air;
                tile.TileType = (ushort)(g is '=' or '(' or ')' or '{' ? 19 : 1);
                tile.IsHalfBlock = shape == TileShape.Half;
                tile.Slope = g switch { '\\' or '(' => (Terraria.ID.SlopeType)1, '/' or ')' => (Terraria.ID.SlopeType)2, '<' => (Terraria.ID.SlopeType)3, '>' => (Terraria.ID.SlopeType)4, _ => 0 };
                tile.LiquidAmount = liquids[i * 2]; tile.LiquidType = liquids[i * 2 + 1];
                if (states != null)
                {
                    byte state = states[i];
                    tile.HasTile = (state & 1) != 0; tile.IsActuated = (state & 2) != 0;
                    tile.TileType = (ushort)(materials![i * 4] | materials[i * 4 + 1] << 8);
                    tile.WallType = (ushort)(materials[i * 4 + 2] | materials[i * 4 + 3] << 8);
                    if (tile.TileType >= Main.tileSolid.Length || tile.TileType >= Main.tileSolidTop.Length)
                        throw new InvalidDataException($"Recorded tile type {tile.TileType} requires a content table unavailable in this native replay; load the matching mod content before claiming exact replay");
                    Main.tileSolid[tile.TileType] = (state & 4) != 0; Main.tileSolidTop[tile.TileType] = (state & 8) != 0;
                    tile.Slope = (Terraria.ID.SlopeType)((state >> 4) & 7); tile.IsHalfBlock = (state & 128) != 0;
                    tile.TileFrameX = (short)(frames![i * 4] | frames[i * 4 + 1] << 8);
                    tile.TileFrameY = (short)(frames[i * 4 + 2] | frames[i * 4 + 3] << 8);
                }
            }
        }
        if (snapshots == 0) throw new InvalidDataException("No terrain snapshots cover the requested body");
        var companion = VerifyCompanionLifecycle.Create();
        Main.player[0].dead = false; Main.player[0].Bottom = Pair("player_px") - offset;
        Vector2 velocity = Pair("observed_vel");
        companion.NPC.Bottom = original - offset + new Vector2(companion.NPC.width / 2f, 0);
        companion.NPC.velocity = velocity;
        companion.NPC.wet = Collision.WetCollision(companion.NPC.position, companion.NPC.width, companion.NPC.height);
        string breath = row["breath"].TrimEnd('u');
        int remaining = (int)MathF.Round(float.Parse(breath, CultureInfo.InvariantCulture) * 200);
        typeof(live::AICompanion.Companion.CharacterBody.CompanionBreath).GetProperty("Breath")!.SetValue(companion.Breath, remaining);
        Console.WriteLine($"CAPTURE {Path.GetFileName(path)} tick={tick}; {snapshots} snapshots from ticks {oldest}..{newest}; offset={ox},{oy}; breath={remaining}");
        Console.WriteLine($"COVERAGE static snapshot replay; uncaptured terrain is closed; {legacySnapshots} snapshots lack exact material state; moving liquids and other entities are not reconstructed; NPC body, full brain and native collision are active.");
        int dry = 0;
        var timings = new List<double>();
        for (int step = 0; step < 1500; step++)
        {
            VerifyObservedMotion.SetTick(Main.GameUpdateCount + 1);
            companion.AI(); VerifyResponsiveFollowing.AdvanceNative(companion);
            timings.Add(companion.Brain.TotalMs);
            Point feet = companion.NPC.Bottom.ToTileCoordinates();
            if (feet.X < 2 || feet.Y < 3 || feet.X >= side - 2 || feet.Y >= side - 2 || !known[feet.X, feet.Y - 1])
            { Console.WriteLine("INCOMPLETE replay reached uncaptured terrain"); return 2; }
            dry = !Collision.DrownCollision(companion.NPC.position, companion.NPC.width, companion.NPC.height, 1f) ? dry + 1 : 0;
            if (step % 120 == 0) Console.WriteLine($"tick+{step}: world-feet={companion.NPC.Bottom + offset}; breath={companion.Breath.Breath}; action={companion.Brain.LastAction?.Name}; controls={companion.Motor.AppliedControls}");
            if (dry >= 60)
            {
                Console.WriteLine($"PASS recorded water: sustained breathing at +{step}, breath={companion.Breath.Breath}");
                timings.Sort();
                Console.WriteLine($"BRAIN TIMING {timings.Count} ticks: median={timings[timings.Count / 2]:0.000}ms p95={timings[(int)((timings.Count - 1) * .95)]:0.000}ms max={timings[^1]:0.000}ms; includes cold initialisation, excludes other game systems");
                return 0;
            }
            if (companion.IsDowned) break;
        }
        Console.WriteLine($"FAIL recorded water: no sustained breathing, world-feet={companion.NPC.Bottom + offset}, breath={companion.Breath.Breath}, action={companion.Brain.LastAction?.Name}");
        return 1;
    }
}
