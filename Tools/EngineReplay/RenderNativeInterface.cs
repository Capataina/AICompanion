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
            SeedVisibleState(assets, preferences);
            Main.mouseX = Main.mouseY = -100;
            using var batch = new SpriteBatch(graphics);
            using var rasterizer = new RasterizerState { ScissorTestEnable = true };
            Main.spriteBatch = batch;
            Main.GameViewMatrix = new Terraria.Graphics.SpriteViewMatrix(graphics);
            VerifyWorldLine(graphics, batch);
            string output = Path.Combine(Path.GetTempPath(), "aic-native-ui"); Directory.CreateDirectory(output);
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
                foreach (string page in new[] { "ShowOverview", "ShowInventory", "ShowMastery", "ShowMasteryWeapon" })
                {
                    string method = page == "ShowMasteryWeapon" ? "ShowMastery" : page;
                    type.GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!.Invoke(card, null); card.Recalculate();
                    card.Update(new GameTime());
                    if (page == "ShowMasteryWeapon") VerifyNativeCard.OpenWeaponPage(card);
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
                typeof(live::AICompanion.Companion.Brain.BehaviourDiagnostics.BrainOverlay)
                    .GetMethod("DrawMenu", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, new object?[] { batch, null });
                batch.End(); graphics.SetRenderTarget(null);
                using (var stream = File.Create(Path.Combine(output, $"Inspector-{suffix}.png")))
                    target.SaveAsPng(stream, size.X, size.Y);
            }
            return 0;
        }
        finally { SDL_DestroyWindow(window); SDL_Quit(); }
    }

    private static void SeedVisibleState(AssetRepository assets, live::AICompanion.Companion.PlayerIntegration.CompanionPlayer preferences)
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
        var mine = new live::AICompanion.Companion.Brain.Behaviours.Work.MineAction();
        var target = new Point(20, 33);
        Tile oreTile = Main.tile[20, 33]; oreTile.HasTile = true; oreTile.TileType = Terraria.ID.TileID.Copper;
        var ore = new live::AICompanion.Companion.Brain.WorldInteractions.Mining.OreFinder.OreTarget(target, Terraria.ID.TileID.Copper, new Vector2(320, 320));
        mine.GetType().GetField("target", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(mine, ore);
        companion.Brain.Chooser.GetType().GetProperty("Current")!.SetValue(companion.Brain.Chooser, mine);
        foreach (var entry in new[] { (Terraria.ID.ItemID.CopperOre, "Copper Ore"), (Terraria.ID.ItemID.Wood, "Wood"), (Terraria.ID.ItemID.Gel, "Gel") })
        {
            TextureAssets.Item[entry.Item1] = assets.Request<Texture2D>("Images/Item_" + entry.Item1, AssetRequestMode.ImmediateLoad);
        }
        TextureAssets.Tile[Terraria.ID.TileID.Copper] = assets.Request<Texture2D>("Images/Tiles_" + Terraria.ID.TileID.Copper, AssetRequestMode.ImmediateLoad);
        for (int i = 0; i < 62; i++)
        {
            int type = i % 3 == 0 ? Terraria.ID.ItemID.CopperOre : i % 3 == 1 ? Terraria.ID.ItemID.Wood : Terraria.ID.ItemID.Gel;
            var item = preferences.Bag.Items[i]; item.SetDefaults(type); item.stack = i + 14;
            item.SetNameOverride(type == Terraria.ID.ItemID.CopperOre ? "Copper Ore" : type == Terraria.ID.ItemID.Wood ? "Wood" : "Gel");
        }
        string status = live::AICompanion.Companion.ProfileCard.DrawCompanionStatus.Describe(companion);
        if (!status.Contains("12 tiles below") || !status.Contains("opportunistic"))
            throw new InvalidOperationException("Action explanation lost retained target or work policy: " + status);
        Console.WriteLine("PASS retained mining evidence: " + status);
        VerifyNativeCard.VerifyMastery();
    }

    private static void VerifyCardNavigation(UIState card) => VerifyNativeCard.VerifyNavigation(card);

    private static void VerifyWorldLine(GraphicsDevice graphics, SpriteBatch batch)
    {
        Main.screenPosition = Vector2.Zero;
        using var target = new RenderTarget2D(graphics, 320, 180);
        graphics.SetRenderTarget(target); graphics.Clear(Color.Transparent);
        batch.Begin();
        typeof(live::AICompanion.Companion.Brain.BehaviourDiagnostics.BrainOverlay)
            .GetMethod("Line", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, new object[] { batch, new Vector2(20, 40), new Vector2(120, 40), Color.White });
        batch.End(); graphics.SetRenderTarget(null);
        var pixels = new Color[320 * 180]; target.GetData(pixels);
        int drawn = pixels.Count(p => p.A != 0);
        if (drawn < 80 || drawn > 400)
            throw new InvalidOperationException($"A 100-pixel debug line painted {drawn} pixels; expected a thin segment");
        Console.WriteLine($"PASS native debug line: {drawn} painted pixels for a 100-pixel segment");
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
        Console.WriteLine("PASS native profile controls: Off/Auto events change the live policy, button bounds remain usable");
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
