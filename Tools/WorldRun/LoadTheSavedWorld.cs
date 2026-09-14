using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using Terraria;
using Terraria.ID;
using Terraria.IO;
using Terraria.ModLoader;

/// <summary>
/// Puts a real saved world into <see cref="Main.tile"/>, so the native body walks the terrain the
/// player actually walked rather than the handful of windows a capture happened to snapshot.
///
/// The tiles come from the game's own reader and nothing else. That is not a convenience: a
/// worldgen cave is full of slopes and half blocks, this repository has already had one whole
/// afternoon eaten by a staircase of slopes that a tile dump drew as a wall, and any parser
/// written here would be re-deciding what a slope is. <see cref="WorldFile.LoadWorldTiles"/> is
/// the routine the game runs on its own save files, so the shapes are the shapes the game had.
///
/// What is *not* used is <see cref="WorldFile.LoadWorld"/>, or the <c>LoadWorld_Version2</c> that
/// sits under it, and the reason is worth writing down because it looks like the obvious route.
/// Both go through <c>LoadHeader</c>, which calls <c>WorldGen.clearWorld</c>, which is a reset of
/// about fifty game subsystems that a running game initialised at startup. Headless they are null,
/// and supplying them is not a bounded job: the chain runs renderer, wiring, then
/// <c>Main.UpdateTimeRate</c>, which reaches <c>CreativePowerManager.Instance</c> and
/// <c>SystemLoader</c> — the content and mod-loading machinery this host deliberately never
/// starts. Three subsystems in, the chain was still growing and had left terrain entirely, so the
/// header is mirrored here instead and the tile section is read directly.
///
/// The mirror is a prefix of the game's own parse, in the game's own order, stopping at the last
/// field this instrument needs. It is checked rather than trusted: the tile section is entered at
/// the offset the file's own position table gives, and after the read the stream must be standing
/// exactly on the next section's offset — which is the same equality the game itself asserts. A
/// drifted mirror therefore refuses the world instead of presenting a misread one.
///
/// What is lost by not running the full loader, stated so nobody looks for it: chests, signs, the
/// town NPC roster, tile entities, the bestiary, and every world flag that lives after
/// <c>rockLayer</c> in the header — hard mode, the downed-boss set, the invasion state. Terrain,
/// liquid, wiring bits, wall and the world's size and layer depths are all present, which is what
/// movement, reach and light read. The modded sidecar (<c>.twld</c>) is not read either; for this
/// mod that holds nothing the terrain needs, because it places vanilla torches and adds no tiles.
/// </summary>
internal static class LoadTheSavedWorld
{
    /// <summary>What a load produced, so a run states its coverage rather than implying it.</summary>
    internal readonly record struct Loaded(string Path, string Name, int Width, int Height, string Hash,
        int SurfaceRow, int RockRow, double Seconds);

    public static Loaded Load(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException($"no world file at {path}", path);
        var clock = Stopwatch.StartNew();

        InitialiseVanillaTileTables();
        InitialiseLoaderHookArrays();
        InitialiseGenerationStrings();

        byte[] bytes = File.ReadAllBytes(path);
        using var reader = new BinaryReader(new MemoryStream(bytes));
        if (!WorldFile.LoadFileFormatHeader(reader, out bool[] importance, out int[] positions))
            throw new InvalidDataException($"{path} is not a world file this game version can read");
        if (positions.Length < 3)
            throw new InvalidDataException($"{path} declares {positions.Length} sections; the tile section's own end offset is not among them");
        int version = BitConverter.ToInt32(bytes, 0);

        MirrorTheHeaderPrefix(reader, version, path);

        Main.tile = (Tilemap)Activator.CreateInstance(typeof(Tilemap),
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public, null,
            new object[] { (ushort)Main.maxTilesX, (ushort)Main.maxTilesY }, null)!;
        Main.ActiveWorldFileData = WorldFile.CreateMetadata(Main.worldName, cloudSave: false, Main.GameMode);
        Main.ActiveWorldFileData.SetWorldSize(Main.maxTilesX, Main.maxTilesY);
        Main.ActiveWorldFileData.UniqueId = UniqueId;
        Main.ActiveWorldFileData.SetSeed(Seed);

        reader.BaseStream.Position = positions[1];
        WorldFile.LoadWorldTiles(reader, importance);
        // The game makes this exact comparison and returns its load-failure code when it fails.
        // Here it is also the only check on the mirrored header, because a wrong mirror is what
        // would otherwise put the reader at the wrong offset with nothing to say so.
        if (reader.BaseStream.Position != positions[2])
            throw new InvalidDataException(
                $"the tile section ended at {reader.BaseStream.Position} and the file says it ends at {positions[2]}; "
                + "the world format has moved and the header mirror in LoadTheSavedWorld must move with it");

        clock.Stop();
        var loaded = new Loaded(path, Main.worldName, Main.maxTilesX, Main.maxTilesY, WorldIdentityHash(),
            (int)Main.worldSurface, (int)Main.rockLayer, clock.Elapsed.TotalSeconds);
        ConfirmTheLayersAgreeWithTheTiles(loaded);
        return loaded;
    }

    private static Guid UniqueId;
    private static string Seed = "";

    /// <summary>
    /// The game's own header fields, in the game's own order, as far as <c>rockLayer</c>.
    ///
    /// Every read here mirrors a line of <c>WorldFile.LoadHeader</c> and the version gates are its
    /// gates, because a field that exists only above some file version shifts every field after it
    /// when it is read unconditionally. It writes into <see cref="Main"/> for the same reason the
    /// game does: the brain reads <c>worldSurface</c> to know what gravity a body is under, and a
    /// world whose surface is wrong is a world whose jumps are all the wrong height.
    /// </summary>
    private static void MirrorTheHeaderPrefix(BinaryReader reader, int version, string path)
    {
        Main.worldName = reader.ReadString();
        if (version >= 179)
        {
            Seed = version == 179 ? reader.ReadInt32().ToString(CultureInfo.InvariantCulture) : reader.ReadString();
            reader.ReadUInt64();                                    // world generator version
        }
        UniqueId = version >= 181 ? new Guid(reader.ReadBytes(16)) : Guid.NewGuid();
        Main.worldID = reader.ReadInt32();
        Main.leftWorld = reader.ReadInt32();
        Main.rightWorld = reader.ReadInt32();
        Main.topWorld = reader.ReadInt32();
        Main.bottomWorld = reader.ReadInt32();
        Main.maxTilesY = reader.ReadInt32();
        Main.maxTilesX = reader.ReadInt32();

        if (version >= 209)
        {
            Main.GameMode = reader.ReadInt32();
            if (version >= 222) Main.drunkWorld = reader.ReadBoolean();
            if (version >= 227) Main.getGoodWorld = reader.ReadBoolean();
            if (version >= 238) Main.tenthAnniversaryWorld = reader.ReadBoolean();
            if (version >= 239) Main.dontStarveWorld = reader.ReadBoolean();
            if (version >= 241) Main.notTheBeesWorld = reader.ReadBoolean();
            if (version >= 249) Main.remixWorld = reader.ReadBoolean();
            if (version >= 266) Main.noTrapsWorld = reader.ReadBoolean();
            if (version >= 267) Main.zenithWorld = reader.ReadBoolean();
            else Main.zenithWorld = Main.remixWorld && Main.drunkWorld;
        }
        else
        {
            Main.GameMode = version >= 112 && reader.ReadBoolean() ? 1 : 0;
            if (version == 208 && reader.ReadBoolean()) Main.GameMode = 2;
        }
        if (version >= 141) reader.ReadInt64();                     // creation time
        Main.moonType = reader.ReadByte();
        for (int i = 0; i < 3; i++) Main.treeX[i] = reader.ReadInt32();
        for (int i = 0; i < 4; i++) Main.treeStyle[i] = reader.ReadInt32();
        for (int i = 0; i < 3; i++) Main.caveBackX[i] = reader.ReadInt32();
        for (int i = 0; i < 4; i++) Main.caveBackStyle[i] = reader.ReadInt32();
        Main.iceBackStyle = reader.ReadInt32();
        Main.jungleBackStyle = reader.ReadInt32();
        Main.hellBackStyle = reader.ReadInt32();
        Main.spawnTileX = reader.ReadInt32();
        Main.spawnTileY = reader.ReadInt32();
        Main.worldSurface = reader.ReadDouble();
        Main.rockLayer = reader.ReadDouble();

        // Cheap refusals before a tilemap is allocated from any of it. A world outside these bounds
        // is a mirror that has lost its place rather than an unusual world, and the allocation that
        // follows would be the last thing this process did.
        if (Main.maxTilesX < 1000 || Main.maxTilesX > 20000 || Main.maxTilesY < 500 || Main.maxTilesY > 10000)
            throw new InvalidDataException($"the header mirror read an implausible world of {Main.maxTilesX}x{Main.maxTilesY} from {path}");
        if (!(Main.worldSurface > 0 && Main.worldSurface < Main.rockLayer && Main.rockLayer < Main.maxTilesY))
            throw new InvalidDataException($"the header mirror read surface {Main.worldSurface} and rock {Main.rockLayer} against a height of {Main.maxTilesY} in {path}");
    }

    /// <summary>
    /// Checks the two layer depths against the tiles that were actually read, because they are the
    /// numbers most able to be wrong quietly.
    ///
    /// Everything else the mirror reads is either bounded by the section-offset check or obviously
    /// nonsense when wrong. The surface and rock depths are not: a plausible-looking pair of
    /// doubles read from the wrong offset still allocates, still loads, and only shows up much
    /// later as a body under the wrong gravity. Sky is mostly air and the rock layer is mostly
    /// stone, so the tiles themselves say whether the two numbers landed where they claim.
    /// </summary>
    private static void ConfirmTheLayersAgreeWithTheTiles(Loaded loaded)
    {
        double sky = SolidFraction(loaded, 8, loaded.SurfaceRow / 2);
        double rock = SolidFraction(loaded, loaded.RockRow + 40, Math.Min(loaded.Height - 8, loaded.RockRow + 240));
        if (sky > 0.25)
            throw new InvalidDataException($"{sky:P0} of sampled tiles above the recorded surface row {loaded.SurfaceRow} are solid; worldSurface did not land where the header mirror put it");
        if (rock < 0.50)
            throw new InvalidDataException($"only {rock:P0} of sampled tiles below the recorded rock row {loaded.RockRow} are solid; rockLayer did not land where the header mirror put it");
    }

    private static double SolidFraction(Loaded loaded, int fromRow, int toRow)
    {
        int solid = 0, sampled = 0;
        for (int y = Math.Max(1, fromRow); y < Math.Min(loaded.Height - 1, toRow); y += 7)
            for (int x = 20; x < loaded.Width - 20; x += 23)
            {
                sampled++;
                if (Main.tile[x, y].HasTile) solid++;
            }
        return sampled == 0 ? 0 : (double)solid / sampled;
    }

    /// <summary>
    /// The vanilla solid, solid-top and block-light tables, built by the game's own initialisers.
    ///
    /// The fixture suites here have never needed them: each builds a scene out of one or two tile
    /// types and states that those are solid. A real world is not dirt, and without these tables
    /// every stone tile in it reads as air to the body, so the companion falls through the world
    /// and every route is trivially clear. The initialisers are private because a running game
    /// calls them once from <c>Main.Initialize</c>; they are pure array writes, so calling them by
    /// name is running the game's own setup rather than approximating it.
    /// </summary>
    private static void InitialiseVanillaTileTables()
    {
        if (Main.tileSolid[TileID.Stone]) return;
        // The torch colour table, which the light scanner reads for every light-emitting tile in
        // the world it scans. A real world has torches in it; the fixture scenes never did.
        TorchID.Initialize();
        foreach (string name in new[] { "Initialize_TileAndNPCData1", "Initialize_TileAndNPCData2" })
            (typeof(Main).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)
                ?? throw new MissingMethodException($"Main.{name} is gone; the vanilla tile tables are built somewhere else now"))
                .Invoke(null, null);

        // Read back rather than assumed: stone is the commonest solid tile in any cave and a
        // platform is the commonest solid-top one, so if either is false the tables did not build
        // and every route below ground would be through open air.
        if (!Main.tileSolid[TileID.Stone] || !Main.tileSolidTop[TileID.Platforms])
            throw new InvalidOperationException("the vanilla tile tables ran and stone is not solid or a platform has no top; the world would be walked as air");
    }

    /// <summary>
    /// Every loader hook array in tModLoader, emptied rather than left null.
    ///
    /// Mod loading builds these; nothing here registers a mod, and the loaders dereference them
    /// without checking. An empty array is the honest state for a host with no mods — every hook
    /// runs over nothing — where a null is a crash in whichever loader is reached first.
    ///
    /// It sweeps the whole namespace rather than naming the loaders one at a time, because naming
    /// them is a patch per case: the tile loader was needed for the brain's mining questions, the
    /// wall loader for the light scanner's wall light, and the next path into the engine would have
    /// needed a third. The sweep costs one pass over the loader types once, and no later addition
    /// to this instrument can be stopped by a loader nobody thought of.
    /// </summary>
    private static void InitialiseLoaderHookArrays()
    {
        // The type list is taken partially on purpose. Some types in this assembly reference
        // libraries a headless host has only as reference assemblies (Steamworks among them), so
        // asking for every type throws with the ones it did resolve attached — and those are the
        // loaders. A total failure to read any type is a different thing and is left to throw.
        Type?[] types;
        try { types = typeof(TileLoader).Assembly.GetTypes(); }
        catch (ReflectionTypeLoadException partial) { types = partial.Types; }
        int filled = 0;

        string loaders = typeof(TileLoader).Namespace!;
        foreach (Type? type in types)
        {
            // Every question asked of a partially-loaded type can itself throw, Namespace included,
            // so the whole inspection of one type is guarded rather than each call inside it. A
            // type that cannot answer what it is is a type this run never reaches.
            try
            {
                if (type is null || !type.IsClass
                    || !type.Name.EndsWith("Loader", StringComparison.Ordinal)
                    || type.Namespace != loaders) continue;
                foreach (FieldInfo field in type.GetFields(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public))
                {
                    if (!field.Name.StartsWith("Hook", StringComparison.Ordinal) || !field.FieldType.IsArray || field.IsLiteral) continue;
                    if (field.GetValue(null) == null)
                        field.SetValue(null, Array.CreateInstance(field.FieldType.GetElementType()!, 0));
                    filled++;
                }
            }
            catch (Exception) { }
        }
        if (filled == 0)
            throw new InvalidOperationException("no loader hook arrays were filled; tModLoader's loader shape has moved and the first engine path into a loader will crash instead");
    }

    /// <summary>
    /// The world-generation strings, which the tile reader writes a progress line into once per
    /// column and which localisation normally fills.
    ///
    /// Nothing here loads localisation, so the array is null and the reader dereferences it on its
    /// very first column — a progress message for a person watching a loading screen taking down a
    /// terrain read that has nothing to do with text. Empty strings are the honest content: the
    /// message is never shown and inventing words for it would put invented text where a reader of
    /// a crash dump would take it for the game's own.
    /// </summary>
    private static void InitialiseGenerationStrings()
    {
        Terraria.Localization.LocalizedText[] lines = Lang.gen;
        for (int i = 0; i < lines.Length; i++)
            lines[i] ??= Terraria.Localization.LocalizedText.Empty;
    }

    /// <summary>
    /// The same hash the recorder writes into a capture's world line, computed the same way, so a
    /// route extracted from a capture can be matched against the world it is replayed in rather
    /// than assumed to be the same one — replaying an old route against a world since mined
    /// through is a false positive nobody would catch.
    ///
    /// It mirrors <c>RecordBrainTelemetry.DescribeWorld</c>, and the arithmetic is written out
    /// rather than taken from <see cref="string.GetHashCode"/> because that one is randomised per
    /// process on .NET Core, so a hash from it agrees with nothing, including itself tomorrow.
    /// </summary>
    public static string WorldIdentityHash()
    {
        WorldFileData? file = Main.ActiveWorldFileData;
        string identity = file == null ? "unknown" : $"{file.UniqueId}|{file.Seed}";
        if (identity == "unknown") return "unknown";
        ulong hash = 14695981039346656037UL;
        foreach (char c in identity) { hash ^= c; hash *= 1099511628211UL; }
        return hash.ToString("x16", CultureInfo.InvariantCulture);
    }
}
