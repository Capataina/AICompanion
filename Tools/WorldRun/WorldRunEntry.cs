using Terraria;

/// <summary>
/// The flag table, kept out of <c>Program.cs</c> so that nothing here runs before the save path is
/// redirected and the library resolver is attached — a top-level statement file's statics are the
/// first thing the runtime touches, and <see cref="Main"/>'s static constructor must not be that
/// thing.
/// </summary>
internal static class WorldRunEntry
{
    public static int Run(string[] args)
    {
        if (Value(args, "--spike-world=") is { } worldPath) return SpikeTheWorldLoader(worldPath);
        Console.Error.WriteLine("usage: --spike-world=<path.wld>");
        return 2;
    }

    internal static string? Value(string[] args, string flag)
        => args.FirstOrDefault(a => a.StartsWith(flag, StringComparison.Ordinal)) is { } found ? found[flag.Length..] : null;

    /// <summary>
    /// Asks the one question the whole instrument rests on: does the game's own world reader run in
    /// this headless host, and what does it cost. Nothing in this repository has ever driven it.
    /// </summary>
    private static int SpikeTheWorldLoader(string path)
    {
        Main.dedServ = true;
        var loaded = LoadTheSavedWorld.Load(path);
        Console.WriteLine($"WORLD {loaded.Name} {loaded.Width}x{loaded.Height} hash={loaded.Hash} in {loaded.Seconds:0.0}s from {loaded.Path}");
        Console.WriteLine($"SURFACE worldSurface={loaded.SurfaceRow} rockLayer={loaded.RockRow} spawn={Main.spawnTileX},{Main.spawnTileY}");

        int solid = 0, liquid = 0, sampled = 0;
        for (int x = 0; x < loaded.Width; x += 37)
            for (int y = 0; y < loaded.Height; y += 37)
            {
                Tile tile = Main.tile[x, y];
                sampled++;
                if (tile.HasTile) solid++;
                if (tile.LiquidAmount > 0) liquid++;
            }
        Console.WriteLine($"TILES sampled={sampled} withTile={solid} withLiquid={liquid}");

        // The light engine reads the local player's blindness before it processes anything, and a
        // headless host has no player slots at all.
        Main.myPlayer = 0;
        for (int i = 0; i < Main.player.Length; i++) Main.player[i] ??= new Player();
        // The light engine reports how long it took into the game's frame profiler on its way out,
        // and the profiler's own arrays are built by an initialiser a running game calls at
        // startup. Without it the area is processed and then the report kills the process.
        TimeLogger.Initialize();
        // ProcessArea is a four-phase state machine, one phase per call, and two of the phases are
        // not lighting at all: the minimap export and the scene-metrics scan. Both have to be
        // survivable or the cycle never reaches the blur that makes light appear.
        Main.Map = new Terraria.Map.WorldMap(Main.maxTilesX, Main.maxTilesY);
        Terraria.Map.MapHelper.Initialize();
        Main.SceneMetrics ??= new Terraria.SceneMetrics();
        Lighting.Initialize();
        int cx = Main.spawnTileX, cy = Main.spawnTileY;
        for (int pass = 1; pass <= 12; pass++)
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            Lighting.LightTiles(cx - 60, cx + 60, cy - 40, cy + 40);
            clock.Stop();
            Console.WriteLine($"LIGHT pass {pass}: 120x80 at spawn in {clock.Elapsed.TotalMilliseconds:0.0}ms; "
                + $"surface brightness={Lighting.Brightness(cx, cy):0.000}; "
                + $"deep brightness={Lighting.Brightness(cx, Main.maxTilesY - 400):0.000}");
        }
        return 0;
    }
}
