extern alias live;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using ReLogic.Content.Readers;
using ReLogic.Content.Sources;
using ReLogic.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.UI;

/// <summary>Renders the production card into a texture using installed Terraria assets.
/// The SDL window is hidden throughout; every graphics resource is owned by this invocation.</summary>
internal static class RenderNativeInterface
{
    [DllImport("SDL2")] private static extern int SDL_Init(uint flags);
    [DllImport("SDL2")] private static extern IntPtr SDL_CreateWindow(string title, int x, int y, int w, int h, uint flags);
    [DllImport("SDL2")] private static extern void SDL_DestroyWindow(IntPtr window);
    [DllImport("SDL2")] private static extern void SDL_Quit();
    [DllImport("FNA3D")] private static extern uint FNA3D_PrepareWindowAttributes();

    public static int Run(string loaderRoot)
    {
        Console.WriteLine("Owned offscreen UI process PID " + Environment.ProcessId);
        typeof(Terraria.Program).GetField("SavePath", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!.SetValue(null, Path.GetTempPath());
        string native = Path.Combine(loaderRoot, "Libraries", "Native", "OSX");
        IntPtr Resolve(string name, Assembly assembly, DllImportSearchPath? search)
        {
            string path = Path.Combine(native, name switch { "SDL2" => "libSDL2-2.0.0.dylib", "FNA3D" => "libFNA3D.0.dylib", _ => "lib" + name + ".dylib" });
            return File.Exists(path) ? NativeLibrary.Load(path) : IntPtr.Zero;
        }
        NativeLibrary.SetDllImportResolver(typeof(RenderNativeInterface).Assembly, Resolve);
        NativeLibrary.SetDllImportResolver(typeof(GraphicsDevice).Assembly, Resolve);
        if (SDL_Init(0x20) != 0) throw new InvalidOperationException("SDL video initialisation failed");
        uint windowFlags = FNA3D_PrepareWindowAttributes();
        IntPtr window = SDL_CreateWindow("Companion offscreen verification", 0, 0, 1280, 720, windowFlags | 0x8);
        if (window == IntPtr.Zero) { SDL_Quit(); throw new InvalidOperationException("Hidden SDL surface creation failed"); }
        try
        {
            var presentation = new PresentationParameters { BackBufferWidth = 1280, BackBufferHeight = 720, DeviceWindowHandle = window, IsFullScreen = false };
            using var graphics = new GraphicsDevice(GraphicsAdapter.DefaultAdapter, GraphicsProfile.Reach, presentation);
            var services = new GameServiceContainer();
            services.AddService(typeof(IGraphicsDeviceService), new GraphicsService(graphics));
            var readers = new AssetReaderCollection();
            readers.RegisterReader(new XnbReader(services), ".xnb");
            string vanilla = Path.Combine(Path.GetDirectoryName(loaderRoot)!, "Terraria", "Terraria.app", "Contents", "Resources", "Content");
            AssetRepository.SetMainThread();
            using var assets = new AssetRepository(readers, new[] { new FileSystemContentSource(vanilla), new FileSystemContentSource(Path.Combine(loaderRoot, "Content")) });
            FontAssets.MouseText = assets.Request<DynamicSpriteFont>("Fonts/Mouse_Text", AssetRequestMode.ImmediateLoad);
            FontAssets.DeathText = assets.Request<DynamicSpriteFont>("Fonts/Death_Text", AssetRequestMode.ImmediateLoad);
            FontAssets.ItemStack = assets.Request<DynamicSpriteFont>("Fonts/Item_Stack", AssetRequestMode.ImmediateLoad);
            TextureAssets.MagicPixel = assets.Request<Texture2D>("Images/MagicPixel", AssetRequestMode.ImmediateLoad);
            TextureAssets.Npc[Terraria.ID.NPCID.Guide] = assets.Request<Texture2D>("Images/NPC_" + Terraria.ID.NPCID.Guide, AssetRequestMode.ImmediateLoad);
            foreach (FieldInfo field in typeof(TextureAssets).GetFields(BindingFlags.Public | BindingFlags.Static))
                if (field.Name.StartsWith("InventoryBack") && field.FieldType == typeof(Asset<Texture2D>))
                {
                    string asset = "Images/Inventory_Back" + field.Name[13..];
                    if (File.Exists(Path.Combine(vanilla, asset + ".xnb")))
                        field.SetValue(null, assets.Request<Texture2D>(asset, AssetRequestMode.ImmediateLoad));
                }
            // UIPanel and UIScrollbar request their native textures through Main.Assets.
            Main.Assets = assets;
            // LoadItem is an instance method even when its asset is already cached.
            // Bypass the Game constructor: this service shell never owns a window.
            Main.instance = (Main)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(Main));
            Main.instance.shop = Enumerable.Range(0, 100).Select(_ => new Chest()).ToArray();
            Terraria.Localization.LanguageManager.Instance.SetLanguage("en-US");
            Main.mouseTextColor = 255;
            Terraria.GameContent.UI.ItemRarity.Initialize();
            Terraria.Initializers.UILinksInitializer.Load();
            Main.npc = Enumerable.Range(0, Main.maxNPCs + 1).Select(_ => new NPC()).ToArray();
            Main.myPlayer = 0;
            Main.player[0] = new Player { active = true };
            var preferences = new live::AICompanion.Companion.PlayerIntegration.CompanionPlayer();
            Terraria.ModLoader.ContentInstance.Register(preferences);
            typeof(Terraria.ModLoader.ModPlayer).GetProperty("Entity", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(preferences, Main.player[0]);
            typeof(Player).GetField("modPlayers", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(Main.player[0], new Terraria.ModLoader.ModPlayer[] { preferences });
            var seeded = SeedVisibleState(assets, preferences);
            Main.mouseX = Main.mouseY = -100;
            using var batch = new SpriteBatch(graphics);
            using var rasterizer = new RasterizerState { ScissorTestEnable = true };
            Main.spriteBatch = batch;
            Main.GameViewMatrix = new Terraria.Graphics.SpriteViewMatrix(graphics);
            VerifyWorldLine(graphics, batch);
            VerifyInspectorDrawsNoSolver();
            // The folder is emptied first, because a run that dies part-way leaves the previous
            // run's images beside its own and a reader cannot tell which page a file documents.
            string output = Path.Combine(Path.GetTempPath(), "aic-native-ui");
            if (Directory.Exists(output)) Directory.Delete(output, recursive: true);
            Directory.CreateDirectory(output);
            foreach (var view in new[] { (new Point(1280, 720), 1f), (new Point(960, 540), 1f), (new Point(800, 600), 1f), (new Point(640, 480), 1f), (new Point(1600, 1000), 1.5f) })
            {
                var (size, scale) = view;
                string suffix = $"{size.X}x{size.Y}" + (scale == 1 ? "" : $"-scale{scale * 100:0}");
                Main.screenWidth = size.X; Main.screenHeight = size.Y;
                Terraria.GameInput.PlayerInput.CacheOriginalScreenDimensions();
                typeof(Main).GetField("_uiScaleUsed", BindingFlags.NonPublic | BindingFlags.Static)!.SetValue(null, scale);
                typeof(Main).GetField("_uiScaleMatrix", BindingFlags.NonPublic | BindingFlags.Static)!.SetValue(null, Matrix.CreateScale(scale, scale, 1));
                Terraria.GameInput.PlayerInput.SetZoom_UI();
                using var target = new RenderTarget2D(graphics, size.X, size.Y);
                var owner = new live::AICompanion.Companion.ProfileCard.CompanionProfileCardSystem();
                Type type = owner.GetType().GetNestedType("CompanionProfileCard", BindingFlags.NonPublic)!;
                var card = (UIState)Activator.CreateInstance(type, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new object[] { owner }, null)!;
                var ui = new UserInterface(); ui.SetState(card); card.Recalculate();
                VerifyProfileControls(card);
                VerifyCardNavigation(card);
                VerifyNativeCard.VerifyInventoryOcclusion(card, owner, ui, graphics, target);
                Rectangle? frameBounds = null;
                foreach (string page in new[] { "ShowOverview", "ShowInventory", "ShowMastery", "ShowMasteryTree" })
                {
                    string method = page == "ShowMasteryTree" ? "ShowMastery" : page;
                    type.GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!.Invoke(card, null); card.Recalculate();
                    card.Update(new GameTime());
                    if (page == "ShowMasteryTree") VerifyNativeCard.OpenDiamondTree(card);
                    Rectangle frame = ((UIElement)type.GetField("frame", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(card)!).GetDimensions().ToRectangle();
                    if (frameBounds is { } previous && previous != frame) throw new InvalidOperationException("Page navigation changed the card's frame");
                    frameBounds = frame;
                    VerifyNativeCard.VerifyPage(card, page);
                    graphics.SetRenderTarget(target); graphics.Clear(new Color(18, 27, 40));
                    batch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, DepthStencilState.None, rasterizer, null, Main.UIScaleMatrix);
                    graphics.ScissorRectangle = new Rectangle(0, 0, size.X, size.Y);
                    ui.Draw(batch, new GameTime()); batch.End();
                    graphics.SetRenderTarget(null);
                    string file = Path.Combine(output, $"{page}-{suffix}.png");
                    using var stream = File.Create(file); target.SaveAsPng(stream, size.X, size.Y);
                    Console.WriteLine("RENDER " + file);
                }
                ui.SetState(null);
                graphics.SetRenderTarget(target); graphics.Clear(new Color(18, 27, 40));
                graphics.ScissorRectangle = new Rectangle(0, 0, size.X, size.Y);
                batch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, DepthStencilState.None, rasterizer, null, Main.UIScaleMatrix);
                typeof(live::AICompanion.Companion.Brain.Infrastructure.Diagnostics.BrainOverlay)
                    .GetMethod("DrawMenu", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, new object?[] { batch, null });
                batch.End(); graphics.SetRenderTarget(null);
                using (var stream = File.Create(Path.Combine(output, $"Inspector-{suffix}.png")))
                    target.SaveAsPng(stream, size.X, size.Y);
                RenderExecutionPage(graphics, batch, rasterizer, size, scale, output, suffix, seeded);
                VerifyCompanionHud.Render(graphics, batch, size, scale, output, suffix);
            }
            // The overlay's region-box check runs last, after every page and the notch have been
            // rendered and saved, so a throw here leaves the images to look at rather than hiding
            // every render behind the first unrelated failure.
            VerifySuccessRegionLayer(graphics, batch);
            return 0;
        }
        finally { SDL_DestroyWindow(window); SDL_Quit(); }
    }

    private static live::AICompanion.Companion.CharacterBody.CompanionNPC SeedVisibleState(AssetRepository assets, live::AICompanion.Companion.PlayerIntegration.CompanionPlayer preferences)
    {
        Main.rand = new Terraria.Utilities.UnifiedRandom(1);
        RecipeGroup.recipeGroups[Terraria.ID.RecipeGroupID.Wood] = new RecipeGroup(() => "Any Wood", Terraria.ID.ItemID.Wood);
        // GetItem refreshes recipes after a transfer. An empty native recipe is the
        // end sentinel; a null array entry is not a valid loaded-game state.
        Main.recipe[0] = (Recipe)Activator.CreateInstance(typeof(Recipe), BindingFlags.Instance | BindingFlags.NonPublic, null, new object?[] { null }, null)!;
        Main.maxTilesX = Main.maxTilesY = 100;
        Main.tile = (Tilemap)Activator.CreateInstance(typeof(Tilemap), BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public, null, new object[] { (ushort)100, (ushort)100 }, null)!;
        Terraria.Map.MapHelper.Initialize();
        var companion = new live::AICompanion.Companion.CharacterBody.CompanionNPC();
        Terraria.ModLoader.ContentInstance.Register(companion);
        var npc = new NPC { active = true, life = 400, lifeMax = 400, position = new Vector2(320, 320), width = 20, height = 42, GivenName = "Aria" };
        typeof(Terraria.ModLoader.ModNPC).GetProperty("Entity", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.SetValue(companion, npc);
        typeof(NPC).GetProperty("ModNPC")!.SetValue(npc, companion);
        typeof(live::AICompanion.Companion.CharacterBody.CompanionBody).GetField("rendererFailed", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(companion.Body, true);
        Main.npc[0] = npc;
        var mine = new live::AICompanion.Companion.Brain.Activities.Gathering.MineOre();
        var target = new Point(20, 33);
        Tile oreTile = Main.tile[20, 33]; oreTile.HasTile = true; oreTile.TileType = Terraria.ID.TileID.Copper;
        var ore = new live::AICompanion.Companion.Brain.Infrastructure.Interactions.Mining.OreFinder.OreTarget(target, Terraria.ID.TileID.Copper, new Vector2(320, 320));
        mine.GetType().GetField("target", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(mine, ore);
        // This is a retained-state rendering fixture, not a discovery run. The public
        // activity target comes from preparation, separately from the native ore target.
        mine.GetType().GetField("preparedTarget", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(mine, target.ToWorldCoordinates());
        companion.Brain.Chooser.Activity.Select(mine,
            new live::AICompanion.Companion.Brain.Activities.ActionContext(companion, companion.Brain.Senses));
        foreach (var entry in new[] { (Terraria.ID.ItemID.CopperOre, "Copper Ore"), (Terraria.ID.ItemID.Wood, "Wood"), (Terraria.ID.ItemID.Gel, "Gel") })
        {
            TextureAssets.Item[entry.Item1] = assets.Request<Texture2D>("Images/Item_" + entry.Item1, AssetRequestMode.ImmediateLoad);
        }
        TextureAssets.Tile[Terraria.ID.TileID.Copper] = assets.Request<Texture2D>("Images/Tiles_" + Terraria.ID.TileID.Copper, AssetRequestMode.ImmediateLoad);
        // The card's portrait is the Destroyer probe, the orb's placeholder body; the live game
        // requests it through Main.instance.LoadNPC, which this uninitialised service shell cannot run.
        TextureAssets.Npc[Terraria.ID.NPCID.Probe] = assets.Request<Texture2D>("Images/NPC_" + Terraria.ID.NPCID.Probe, AssetRequestMode.ImmediateLoad);
        for (int i = 0; i < 62; i++)
        {
            int type = i % 3 == 0 ? Terraria.ID.ItemID.CopperOre : i % 3 == 1 ? Terraria.ID.ItemID.Wood : Terraria.ID.ItemID.Gel;
            var item = preferences.Bag.Items[i]; item.SetDefaults(type); item.stack = i + 14;
            item.SetNameOverride(type == Terraria.ID.ItemID.CopperOre ? "Copper Ore" : type == Terraria.ID.ItemID.Wood ? "Wood" : "Gel");
        }
        string status = live::AICompanion.Companion.ProfileCard.DrawCompanionStatus.Describe(companion);
        if (!status.Contains("12 tiles below") || !status.Contains("opportunistic"))
            throw new InvalidOperationException("Action explanation lost retained target or work policy: " + status);
        Console.WriteLine("retained mining evidence: " + status);
        VerifyNativeCard.VerifyMastery();
        SeedExecutionEvidence(companion, mine, target);
        return companion;
    }

    /// <summary>
    /// Retained results the Execution page reads, set as the brain would have left them: one Gathering nomination over a
    /// usable mining offer and an unresolved chopping one, a tool region for the seeded ore, a grant applied under a different
    /// owner than it was requested, and a concluded attempt. This is rendering state, not a decision run: nothing here asks
    /// the chooser, the positioner or the finaliser to compute anything.
    /// </summary>
    private static void SeedExecutionEvidence(live::AICompanion.Companion.CharacterBody.CompanionNPC companion,
        live::AICompanion.Companion.Brain.Activities.Gathering.MineOre mine, Point tile)
    {
        var brain = companion.Brain;
        var chooser = brain.Chooser;
        var chop = chooser.Actions.First(action => action.Name == "chop");
        chooser.LastScores.Clear();
        chooser.LastScores.Add(new(mine, .80f, .80f, Eligibility: live::AICompanion.Companion.Brain.Activities.OfferEligibility.Usable, EligibilityReason: "proven-pose"));
        chooser.LastScores.Add(new(chop, .40f, 0f, Eligibility: live::AICompanion.Companion.Brain.Activities.OfferEligibility.Unresolved, EligibilityReason: "approach-undecided"));
        typeof(live::AICompanion.Companion.Brain.Infrastructure.Selection.Chooser).GetProperty("LastNominations")!.SetValue(chooser, new[]
        {
            new live::AICompanion.Companion.Brain.Infrastructure.Selection.FamilyNomination(live::AICompanion.Companion.Brain.Infrastructure.Selection.PurposeFamily.Gathering,
                new live::AICompanion.Companion.Brain.Infrastructure.Selection.EvaluatedActivity(0, "mine", .80f, .80f, 1f, 1f, 1f, 1f, "")),
        });
        var region = live::AICompanion.Companion.Brain.Infrastructure.Position.SuccessRegion.ToolStand(new Vector2(320, 320), tile, 100, 1);
        brain.Positioner.GetType().GetProperty("Region")!.SetValue(brain.Positioner, region);
        brain.ControlGrants.GetType().GetProperty("Last")!.SetValue(brain.ControlGrants,
            new live::AICompanion.Companion.Brain.Infrastructure.Grants.ActivityControlGrant(3, 100, 1, live::AICompanion.Companion.Brain.Infrastructure.Selection.ActivityPhase.Suspended,
                "downed", "travel-recovery-clearance", default, default, live::AICompanion.Companion.Brain.Infrastructure.Grants.HandGrant.Unavailable, null, Vector2.Zero, 1));
        var recent = (System.Collections.IList)chooser.Activity.GetType().GetField("recent", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(chooser.Activity)!;
        recent.Add(new live::AICompanion.Companion.Brain.Activities.AttemptOutcome(7, 1, "mine", live::AICompanion.Companion.Brain.Infrastructure.Selection.PurposeFamily.Gathering,
            90, 100, live::AICompanion.Companion.Brain.Activities.AttemptStatus.Complete, "tracked-vein-observed-clear", 3,
            live::AICompanion.Companion.Brain.Activities.AttemptAttribution.Companion));
    }

    /// <summary>
    /// The inspector reads retained evidence and never asks for a new answer: neither drawing file may call the positioner,
    /// a route or reach search, the local planner or the aimer. A call added later fails here before it can make drawing
    /// change a decision or cost a solve.
    /// </summary>
    private static void VerifyInspectorDrawsNoSolver()
    {
        string folder = Path.Combine("Companion", "Brain", "Infrastructure", "Diagnostics");
        foreach (string file in new[] { "DrawBrainOverlay.cs", "DescribeExecutionEvidence.cs" })
        {
            string source = File.ReadAllText(Path.Combine(folder, file));
            foreach (string call in new[] { ".Resolve(", "PrepareOffer(", "InReach(", "Approach(", "CanReach(", "WalkerReach(", "MoveTo(", "SeekDestination(", ".Plan(", "Solve(" })
                if (source.Contains(call, StringComparison.Ordinal))
                    throw new InvalidOperationException($"{file} calls {call}, so drawing the inspector would compute an answer instead of showing a retained one");
        }
        Console.WriteLine("inspector drawing calls no positioner, planner, aimer or reach test");
    }

    /// <summary>
    /// The success-region layer, checked in geometry and in pixels. Points half a pixel inside and one pixel outside every box
    /// edge must agree with the region's own containment test and, for a tool stand, with the reach arithmetic InReach uses;
    /// then a drawn tool box must paint its perimeter, leave its corner interiors empty and paint nothing outside it.
    /// </summary>
    private static void VerifySuccessRegionLayer(GraphicsDevice graphics, SpriteBatch batch)
    {
        int reachX = Player.tileRangeX, reachY = Player.tileRangeY;
        Player.tileRangeX = 5; Player.tileRangeY = 4;
        try
        {
            var tile = new Point(20, 20);
            var tool = live::AICompanion.Companion.Brain.Infrastructure.Position.SuccessRegion.ToolStand(new Vector2(20 * 16 + 8, 23 * 16), tile, 100, 1);
            var follow = live::AICompanion.Companion.Brain.Infrastructure.Position.SuccessRegion.Follow(
                new live::AICompanion.Companion.Brain.Infrastructure.Position.FollowPlayerObjective(
                    new live::AICompanion.Companion.Brain.Infrastructure.Observation.PlayerIntentRegion(
                        new Vector2(600, 400), new Vector2(240, 96), Vector2.Zero, IsTravelling: false),
                    new Vector2(900, 400), Settled: true, Grounded: true), 100, 1);
            int judged = 0, outside = 0;
            foreach (var region in new[] { tool, follow })
            {
                var boxes = live::AICompanion.Companion.Brain.Infrastructure.Diagnostics.DescribeExecutionEvidence.RegionBoxes(region);
                if (boxes.Count == 0) throw new InvalidOperationException($"a {region.Kind} region declared no box to draw");
                foreach (var box in boxes)
                {
                    Vector2 mid = (box.Min + box.Max) / 2;
                    foreach (Vector2 point in new[] {
                        new Vector2(box.Min.X + .5f, mid.Y), new Vector2(box.Max.X - .5f, mid.Y), new Vector2(mid.X, box.Min.Y + .5f), new Vector2(mid.X, box.Max.Y - .5f),
                        new Vector2(box.Min.X - 1, mid.Y), new Vector2(box.Max.X + 1, mid.Y), new Vector2(mid.X, box.Min.Y - 1), new Vector2(mid.X, box.Max.Y + 1) })
                    {
                        bool drawn = boxes.Any(b => point.X >= b.Min.X && point.X <= b.Max.X && point.Y >= b.Min.Y && point.Y <= b.Max.Y);
                        if (region.Contains(point) != drawn)
                            throw new InvalidOperationException($"the drawn {region.Kind} boxes say {point} is {(drawn ? "inside" : "outside")}, but the region's own test disagrees");
                        if (region.Kind == live::AICompanion.Companion.Brain.Infrastructure.Position.SuccessRegionKind.ToolReach
                            && live::AICompanion.Companion.Brain.Infrastructure.Interactions.FindToolAccess.InReachBox(point, tile, 5, 4) != drawn)
                            throw new InvalidOperationException($"the drawn reach box says {point} is {(drawn ? "inside" : "outside")}, but the reach arithmetic disagrees");
                        judged++;
                        if (!drawn) outside++;
                    }
                }
            }
            if (outside == 0) throw new InvalidOperationException("no sampled point fell outside a drawn box, so the edge comparison measured nothing");

            var toolBox = live::AICompanion.Companion.Brain.Infrastructure.Diagnostics.DescribeExecutionEvidence.RegionBoxes(tool)[0];
            Main.screenPosition = toolBox.Min - new Vector2(40, 40);
            using var target = new RenderTarget2D(graphics, 320, 240);
            graphics.SetRenderTarget(target); graphics.Clear(Color.Transparent);
            batch.Begin();
            typeof(live::AICompanion.Companion.Brain.Infrastructure.Diagnostics.BrainOverlay)
                .GetMethod("DrawRegion", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, new object[] { batch, tool });
            batch.End(); graphics.SetRenderTarget(null);
            Main.screenPosition = Vector2.Zero;
            var pixels = new Color[320 * 240]; target.GetData(pixels);
            bool Painted(int x, int y) => pixels[y * 320 + x].A != 0;
            var frame = new Rectangle(40, 40, (int)MathF.Round(toolBox.Max.X - toolBox.Min.X), (int)MathF.Round(toolBox.Max.Y - toolBox.Min.Y));
            int perimeter = 0, covered = 0;
            for (int x = frame.Left; x < frame.Right; x++) { perimeter += 2; covered += (Painted(x, frame.Top) ? 1 : 0) + (Painted(x, frame.Bottom - 1) ? 1 : 0); }
            for (int y = frame.Top + 1; y < frame.Bottom - 1; y++) { perimeter += 2; covered += (Painted(frame.Left, y) ? 1 : 0) + (Painted(frame.Right - 1, y) ? 1 : 0); }
            int strays = 0;
            for (int y = 0; y < 240; y++)
                for (int x = 0; x < 320; x++)
                    if (Painted(x, y) && (x < frame.Left || x >= frame.Right || y < frame.Top || y >= frame.Bottom)) strays++;
            int cornerPaint = 0;
            foreach (var corner in new[] { new Point(frame.Left + 4, frame.Top + 4), new Point(frame.Right - 24, frame.Top + 4), new Point(frame.Left + 4, frame.Bottom - 24), new Point(frame.Right - 24, frame.Bottom - 24) })
                for (int y = corner.Y; y < corner.Y + 20; y++)
                    for (int x = corner.X; x < corner.X + 20; x++)
                        if (Painted(x, y)) cornerPaint++;
            if (covered != perimeter || strays != 0 || cornerPaint != 0)
                throw new InvalidOperationException($"the drawn reach box painted {covered} of its {perimeter} perimeter pixels, {strays} outside it and {cornerPaint} inside its corners");
            Console.WriteLine($"success region layer: {judged} edge samples agree with the region test and the reach arithmetic ({outside} outside); the drawn reach box paints all {perimeter} perimeter pixels and nothing outside or in its corners");
        }
        finally { Player.tileRangeX = reachX; Player.tileRangeY = reachY; }
    }

    /// <summary>
    /// The inspector's Execution tab rendered from the seeded retained state at one viewport. Its lines must carry each seeded
    /// fact, its visible lines must end above the footer, and its tabs must cover the strip exactly; at scale 1 the heading
    /// must carry gold text, the Execution tab must be the highlighted one and the background outside the panel untouched.
    /// </summary>
    private static void RenderExecutionPage(GraphicsDevice graphics, SpriteBatch batch, RasterizerState rasterizer, Point size, float scale,
        string output, string suffix, live::AICompanion.Companion.CharacterBody.CompanionNPC companion)
    {
        Type overlay = typeof(live::AICompanion.Companion.Brain.Infrastructure.Diagnostics.BrainOverlay);
        FieldInfo page = overlay.GetField("page", BindingFlags.Static | BindingFlags.NonPublic)!;
        using var target = new RenderTarget2D(graphics, size.X, size.Y);
        var background = new Color(18, 27, 40);
        page.SetValue(null, 2);
        try
        {
            graphics.SetRenderTarget(target); graphics.Clear(background);
            graphics.ScissorRectangle = new Rectangle(0, 0, size.X, size.Y);
            batch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, DepthStencilState.None, rasterizer, null, Main.UIScaleMatrix);
            overlay.GetMethod("DrawMenu", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, new object?[] { batch, companion });
            batch.End(); graphics.SetRenderTarget(null);
        }
        finally { page.SetValue(null, 0); }
        using (var stream = File.Create(Path.Combine(output, $"InspectorExecution-{suffix}.png")))
            target.SaveAsPng(stream, size.X, size.Y);

        var lines = live::AICompanion.Companion.Brain.Infrastructure.Diagnostics.DescribeExecutionEvidence.Of(companion.Brain);
        string text = string.Join("\n", lines.Select(line => line.Text));
        foreach (string expected in new[] { "Gathering: nominated mine at 0.80", "  mine Usable:proven-pose raw 0.80 final 0.80", "  chop Unresolved:approach-undecided",
            "to tile 20,33 from stand 320.00,320.00", "requested downed, applied travel-recovery-clearance  (differs)", "hand Unavailable",
            "last attempt 7 mine: Complete:Companion by tracked-vein-observed-clear, ticks 90..100", "3 productive effect(s); no yield claimed" })
            if (!text.Contains(expected, StringComparison.Ordinal))
                throw new InvalidOperationException($"the Execution evidence lost '{expected}':\n{text}");

        Rectangle panel = live::AICompanion.Companion.Brain.Infrastructure.Diagnostics.BrainOverlay.PanelBounds((int)(size.X / scale), (int)(size.Y / scale));
        int visible = live::AICompanion.Companion.Brain.Infrastructure.Diagnostics.BrainOverlay.VisibleExecutionLines(panel);
        Rectangle lastLine = live::AICompanion.Companion.Brain.Infrastructure.Diagnostics.BrainOverlay.ExecutionLineBounds(panel, visible - 1);
        Rectangle firstTab = live::AICompanion.Companion.Brain.Infrastructure.Diagnostics.BrainOverlay.TabBounds(panel, 0);
        Rectangle executionTab = live::AICompanion.Companion.Brain.Infrastructure.Diagnostics.BrainOverlay.TabBounds(panel, 2);
        if (lastLine.Bottom > panel.Bottom - 20 || firstTab.Left != panel.X + 12 || executionTab.Right != panel.Right - 12)
            throw new InvalidOperationException($"the Execution page overflows its panel {panel}: last visible line {lastLine}, tabs {firstTab}..{executionTab}");
        if (scale == 1f)
        {
            var pixels = new Color[size.X * size.Y]; target.GetData(pixels);
            Color At(int x, int y) => pixels[y * size.X + x];
            bool Near(Color a, Color b) => Math.Abs(a.R - b.R) <= 2 && Math.Abs(a.G - b.G) <= 2 && Math.Abs(a.B - b.B) <= 2;
            Rectangle heading = live::AICompanion.Companion.Brain.Infrastructure.Diagnostics.BrainOverlay.ExecutionLineBounds(panel, 0);
            bool gold = false;
            for (int y = heading.Top; y < heading.Bottom && !gold; y++)
                for (int x = heading.Left; x < heading.Right && !gold; x++)
                    if (At(x, y) is { R: > 180, G: > 140, B: < 120 }) gold = true;
            var highlight = new Color(66, 88, 151);
            if (!gold || !Near(At(executionTab.Right - 3, executionTab.Bottom - 3), highlight) || Near(At(firstTab.Right - 3, firstTab.Bottom - 3), highlight)
                || (panel.Right + 10 < size.X && panel.Bottom + 10 < size.Y && !Near(At(panel.Right + 10, panel.Bottom + 10), background)))
                throw new InvalidOperationException($"the rendered Execution page at {suffix} lacks its gold heading ({gold}), its highlighted tab, or an untouched background outside the panel");
        }
        Console.WriteLine($"inspector execution page {suffix}: {lines.Count} retained evidence lines, {visible} visible inside the panel{(scale == 1f ? ", heading, tab and background pixels as drawn" : "")}");
    }

    private static void VerifyCardNavigation(UIState card) => VerifyNativeCard.VerifyNavigation(card);

    private static void VerifyWorldLine(GraphicsDevice graphics, SpriteBatch batch)
    {
        Main.screenPosition = Vector2.Zero;
        using var target = new RenderTarget2D(graphics, 320, 180);
        graphics.SetRenderTarget(target); graphics.Clear(Color.Transparent);
        batch.Begin();
        typeof(live::AICompanion.Companion.Brain.Infrastructure.Diagnostics.BrainOverlay)
            .GetMethod("Line", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, new object[] { batch, new Vector2(20, 40), new Vector2(120, 40), Color.White });
        batch.End(); graphics.SetRenderTarget(null);
        var pixels = new Color[320 * 180]; target.GetData(pixels);
        int drawn = pixels.Count(p => p.A != 0);
        if (drawn < 80 || drawn > 400)
            throw new InvalidOperationException($"A 100-pixel debug line painted {drawn} pixels; expected a thin segment");
        Console.WriteLine($"native debug line: {drawn} painted pixels for a 100-pixel segment");
    }

    private static void VerifyProfileControls(UIElement card)
    {
        IEnumerable<UIElement> Descendants(UIElement parent)
        {
            foreach (UIElement child in parent.Children)
            { yield return child; foreach (var nested in Descendants(child)) yield return nested; }
        }
        var buttons = Descendants(card).OfType<Terraria.GameContent.UI.Elements.UITextPanel<string>>().ToArray();
        var off = buttons.First(b => b.Text == "Off");
        off.LeftClick(new UIMouseEvent(off, off.GetDimensions().Center()));
        if ((int)live::AICompanion.Companion.PlayerIntegration.CompanionPreferences.Current.Mining != 0)
            throw new InvalidOperationException("The native mining Off button did not change the live preference");
        var auto = buttons.First(b => b.Text == "Auto");
        auto.LeftClick(new UIMouseEvent(auto, auto.GetDimensions().Center()));
        if ((int)live::AICompanion.Companion.PlayerIntegration.CompanionPreferences.Current.Mining != 2)
            throw new InvalidOperationException("The native mining Auto button did not restore the live preference");
        foreach (var button in buttons)
        {
            var bounds = button.GetDimensions();
            if (bounds.Width < 20 || bounds.Height < 20)
                throw new InvalidOperationException($"Native control {button.Text} has unusable bounds {bounds}");
        }
        Console.WriteLine("native profile controls: Off/Auto events change the live policy, button bounds remain usable");
    }

    private sealed class GraphicsService(GraphicsDevice device) : IGraphicsDeviceService
    {
        public GraphicsDevice GraphicsDevice => device;
        public event EventHandler<EventArgs>? DeviceCreated { add { } remove { } }
        public event EventHandler<EventArgs>? DeviceDisposing { add { } remove { } }
        public event EventHandler<EventArgs>? DeviceReset { add { } remove { } }
        public event EventHandler<EventArgs>? DeviceResetting { add { } remove { } }
    }
}
