extern alias live;
using System.Reflection;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using ReLogic.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.GameContent.UI.Elements;
using Terraria.ID;
using Terraria.UI;
using CardSystem = live::AICompanion.Companion.ProfileCard.CompanionProfileCardSystem;
using Status = live::AICompanion.Companion.ProfileCard.DrawCompanionStatus;
using Segments = live::AICompanion.Companion.ProfileCard.JoinedSegments;
using Primitives = live::AICompanion.Companion.ProfileCard.DrawCardPrimitives;
using Portrait = live::AICompanion.Companion.ProfileCard.DrawDronePortrait;
using MiningPage = live::AICompanion.Companion.ProfileCard.ShowMiningList;
using Mastery = live::AICompanion.Companion.ProfileCard.PreviewMasteryTree;
using Graph = live::AICompanion.Companion.ProfileCard.DefineMasteryGraph;
using Bag = live::AICompanion.Companion.Inventory.CompanionBagUI;
using Preferences = live::AICompanion.Companion.PlayerIntegration.CompanionPreferences;
using Hud = live::AICompanion.Companion.HeadsUpDisplay.CompanionHealthBar;
using CardButton = live::AICompanion.Companion.ProfileCard.CardButton;

/// <summary>
/// The companion card at one render viewport: every region measured against the mock's logical sizes, the 12px rhythm
/// between stacked regions computed from the layout rectangles, no two sibling regions overlapping, the placement rule
/// against a docked notch, then each page driven through <see cref="VerifyNativeCard"/> and rendered to a PNG, with
/// pixel checks at scale 1 for what a rectangle cannot show: the drone drawn, the icons painted, dim ores dimmer, zoom in
/// and zoom out distinguishable, and a lone level segment rounded at both ends.
///
/// <para>The expected numbers are written here from the mock and the owner's rulings, deliberately not read from the
/// production <c>CardRegions</c> table, so a drift in that table fails this file instead of moving both together.</para>
/// </summary>
internal static class RenderCompanionCard
{
    private const int FrameWidth = 720, OverviewHeight = 240, PageHeight = 580, Inset = 12, TitleHeight = 34, Rhythm = 12;
    private const int IdentityHeight = 100, TileHeight = 56, ContentHeight = 508, InnerWidth = 696, Tolerance = 1;
    /// <summary>What a page spends outside its content: the frame's padding, the title bar, one rhythm and the two-pixel border allowance.</summary>
    private const int PageOverhead = PageHeight - ContentHeight;
    /// <summary>The bag page's fixed rows around its grid: the gear boxes, a rhythm above and below the grid, and the bottom line.</summary>
    private const int BagRowsAroundGrid = 64 + Rhythm + Rhythm + 24;

    /// <summary>
    /// A page's height at a frame top and viewport: the mock's 580 wherever that fits under the frame, otherwise everything
    /// from the frame's top to the screen's bottom edge, and never shorter than the overview. The card keeps its title bar
    /// where the overview put it, so a page too tall for the screen shortens instead of running past the bottom edge.
    /// </summary>
    /// <param name="top">The frame's unrounded top in UI units; at a fractional UI scale a rounded top moves the answer by a pixel.</param>
    private static float PageHeightAt(float top, Vector2 view) => Math.Min(PageHeight, Math.Max(OverviewHeight, view.Y - top));
    private static readonly Color Background = new(18, 27, 40);
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    /// <summary>Items whose icons the card draws and ores the mining list knows, loaded once because this shell cannot request assets.</summary>
    private static readonly int[] IconItems = { ItemID.CopperPickaxe, ItemID.CopperAxe, ItemID.Torch, ItemID.CopperBroadsword };
    private static readonly int[] KnownOreItems =
    {
        ItemID.CopperOre, ItemID.TinOre, ItemID.IronOre, ItemID.LeadOre, ItemID.SilverOre, ItemID.TungstenOre, ItemID.GoldOre, ItemID.PlatinumOre,
        ItemID.DemoniteOre, ItemID.CrimtaneOre, ItemID.Meteorite, ItemID.Hellstone, ItemID.CobaltOre, ItemID.PalladiumOre, ItemID.MythrilOre,
    };
    /// <summary>The two ores seeded as marked, so the render shows a dim ore beside bright ones under Skip marked.</summary>
    internal static readonly ushort[] MarkedOres = { TileID.Iron, TileID.Meteorite };

    public static void Seed(AssetRepository assets, live::AICompanion.Companion.PlayerIntegration.CompanionPlayer owner)
    {
        foreach (int item in IconItems)
            TextureAssets.Item[item] = assets.Request<Texture2D>("Images/Item_" + item, AssetRequestMode.ImmediateLoad);
        Player player = Main.LocalPlayer;
        Item[] saved = player.inventory.Select(item => item.Clone()).ToArray();
        try
        {
            foreach (Item item in player.inventory) item.TurnToAir();
            for (int i = 0; i < KnownOreItems.Length; i++) player.inventory[10 + i].SetDefaults(KnownOreItems[i]);
            Preferences.Current = owner.Preferences;
            owner.Preferences.MiningList.RecordHeld(player);
        }
        finally { for (int i = 0; i < saved.Length; i++) player.inventory[i] = saved[i]; }
        foreach (int tile in owner.Preferences.MiningList.Known)
            TextureAssets.Tile[tile] = assets.Request<Texture2D>("Images/Tiles_" + tile, AssetRequestMode.ImmediateLoad);
        foreach (ushort tile in MarkedOres) owner.Preferences.MiningList.SetMarked(tile, true);
        Require(owner.Preferences.MiningList.Known.Count == KnownOreItems.Length,
            $"the seeded mining list must know every ore item placed in the inventory; knows {owner.Preferences.MiningList.Known.Count} of {KnownOreItems.Length}");
        owner.Gear.Slots[0].SetDefaults(ItemID.CopperBroadsword);
        owner.Gear.Slots[2].SetDefaults(ItemID.CopperPickaxe);
        owner.Gear.Slots[3].SetDefaults(ItemID.CopperAxe);
        Console.WriteLine($"card seed: {owner.Preferences.MiningList.Known.Count} known ores, {MarkedOres.Length} marked, a sword, pickaxe and axe in the gear slots");
    }

    public static void Run(GraphicsDevice graphics, SpriteBatch batch, RasterizerState rasterizer, RenderTarget2D target, Point size, float scale, string output, string suffix)
    {
        int minWidth = (int)(typeof(Main).GetField("minScreenW", BindingFlags.Public | BindingFlags.Static)?.GetValue(null) ?? 0);
        int minHeight = (int)(typeof(Main).GetField("minScreenH", BindingFlags.Public | BindingFlags.Static)?.GetValue(null) ?? 0);
        bool belowMinimum = size.X < minWidth || size.Y < minHeight;
        var owner = new CardSystem();
        Type type = owner.GetType().GetNestedType("CompanionProfileCard", BindingFlags.NonPublic)!;
        var card = (UIState)Activator.CreateInstance(type, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new object[] { owner }, null)!;
        var ui = new UserInterface(); ui.SetState(card); card.Recalculate(); card.Update(new GameTime());
        var frameElement = (UIElement)Field(card, "frame");
        var save = Main.LocalPlayer.GetModPlayer<live::AICompanion.Companion.PlayerIntegration.CompanionPlayer>();
        Require(save.HealthBarPosition == null, "premise: the card is measured against a docked notch");
        var view = new Vector2(size.X, size.Y) / scale;
        // Each group of checks records its failure and the run carries on to the next group and to every render, so one run
        // shows every red at this viewport instead of the first; the viewport throws once, at its end, if anything failed.
        var failures = new List<string>();
        void Step(string name, Action action)
        {
            try { action(); }
            catch (InvalidOperationException error)
            {
                failures.Add($"{name}: {error.Message}");
                Console.WriteLine($"CARD CHECK FAILED {suffix} {name}: {error.Message}");
            }
        }

        // A page sitting open allocates nothing of the card's own: what a frame takes from the heap is the game font's fixed
        // cost per glyph and nothing more. At the first viewport the sources are printed by element as well.
        void Allocation(string page)
        {
            var (perFrame, glyphsPerFrame, perGlyph, ownPerFrame) = MeasureCardAllocation(graphics, batch, rasterizer, target, ui);
            Console.WriteLine($"allocation {suffix} {page}: {perFrame} bytes per frame, {glyphsPerFrame} glyphs a frame at {perGlyph} bytes each through the game's font, {ownPerFrame} bytes per frame the card's own");
            if (size == new Point(1280, 720)) Console.WriteLine($"allocation sources {suffix} {page}: {AllocationByElement(graphics, batch, rasterizer, target, ui, card)}");
            Step($"allocation on the {page} page", () => Require(ownPerFrame <= OwnBytesPerFrameAllowed,
                $"{suffix}: a frame on the {page} page allocates {ownPerFrame} bytes beyond the font's {perGlyph} per glyph ({perFrame} in all for {glyphsPerFrame} glyphs); the card's own drawing and update must allocate nothing per frame"));
        }
        if (size == new Point(1280, 720)) Console.WriteLine($"allocation floor {suffix}: {GameTextCost(graphics, batch, rasterizer, target)}");

        // ---- placement: centred under the docked notch, never closer to it than the clearance ----
        float notchBottom = Hud.Bounds(save).Bottom / scale;
        Rectangle frame = Rect(frameElement);
        Step("placement", () =>
        {
            float expectedX = Math.Clamp((view.X - FrameWidth) / 2, 0, Math.Max(0, view.X - FrameWidth));
            float expectedY = Math.Max(notchBottom + Rhythm, Math.Min(notchBottom + 24, Math.Max(0, view.Y - OverviewHeight)));
            Require(Math.Abs(frame.X - expectedX) <= Tolerance && Math.Abs(frame.Y - expectedY) <= Tolerance,
                $"{suffix}: the card opened at {frame.Location}, where the rule puts it at ({expectedX}, {expectedY}) under a notch ending at {notchBottom}");
            Require(frame.Y >= notchBottom + Rhythm - Tolerance, $"{suffix}: the title bar rose under the docked notch");
        });

        // ---- overview geometry ----
        var title = (UIElement)Field(card, "titleBar");
        var identity = (Status)Field(card, "identity");
        var footer = (UIElement)Field(card, "footer");
        var controls = identity.Controls;
        var measures = new List<string>();
        Step("overview geometry", () =>
        {
        Require(frame.Width == FrameWidth && frame.Height == OverviewHeight, $"{suffix}: overview is {frame.Width}x{frame.Height}, not {FrameWidth}x{OverviewHeight}");
        Expect(measures, "title bar", Rect(title), frame, Inset, Inset, InnerWidth, TitleHeight);
        Require(((UIElement)Field(card, "back")).Parent == null, $"{suffix}: the overview's title bar must have no back button, because there is nothing to go back to");
        Expect(measures, "close", Rect((UIElement)Field(card, "close")), frame, Inset + InnerWidth - 30, Inset, 30, 30);
        Expect(measures, "identity strip", Rect(identity), frame, Inset, Inset + TitleHeight + Rhythm, InnerWidth, IdentityHeight);
        Expect(measures, "tiles", Rect(footer), frame, Inset, Inset + TitleHeight + Rhythm + IdentityHeight + Rhythm, InnerWidth, TileHeight);
        RequireRhythm(suffix, "title bar", Rect(title), "identity strip", Rect(identity));
        RequireRhythm(suffix, "identity strip", Rect(identity), "tiles", Rect(footer));
        Require(frame.Bottom - Rect(footer).Bottom == Inset + 2, $"{suffix}: the tiles end {frame.Bottom - Rect(footer).Bottom}px above the frame's edge, not the mock's {Inset + 2}");
        Rectangle[] tiles = footer.Children.Select(Rect).OrderBy(r => r.X).ToArray();
        Require(tiles.Length == 3, "the overview must carry exactly three tiles");
        Require(tiles[0].Left == Rect(footer).Left && Math.Abs(tiles[2].Right - Rect(footer).Right) <= Tolerance
            && tiles.All(t => Math.Abs(t.Width - (InnerWidth - 20) / 3f) <= Tolerance && t.Height == TileHeight)
            && Math.Abs(tiles[1].Left - tiles[0].Right - 10) <= Tolerance && Math.Abs(tiles[2].Left - tiles[1].Right - 10) <= Tolerance,
            $"{suffix}: the tiles must be three equal widths with 10px gaps across the strip: {string.Join(" ", tiles)}");
        RequireNoOverlap(suffix, ("title bar", Rect(title)), ("identity strip", Rect(identity)), ("tiles", Rect(footer)));

        // Inside the strip: portrait, bars with their readings, and the controls, left to right without touching.
        Rectangle strip = Rect(identity);
        Rectangle portrait = Status.Portrait(strip);
        Rectangle[] bars = Status.Bars(strip);
        Expect(measures, "portrait", portrait, strip, 2, 3, 66, 92);
        for (int i = 0; i < 3; i++) Expect(measures, $"bar {i}", bars[i], strip, 80, 30 + 25 * i, 220, 16);
        Require(bars[2].Bottom <= strip.Bottom, $"{suffix}: the experience bar runs out of the strip");
        float widestReading = new[] { "400 / 400 HP", "1,000 / 1,000 MP", "10,000 / 10,000 XP" }
            .Max(text => FontAssets.MouseText.Value.MeasureString(text).X * Status.ReadingScale);
        Rectangle controlsRect = Rect(controls);
        Require(controlsRect.Left == strip.Left + 430 && controlsRect.Right == strip.Right, $"{suffix}: the controls must span x 430 to the strip's right edge: {controlsRect}");
        Require(bars[0].Right + Status.ReadingGap + widestReading < controlsRect.Left,
            $"{suffix}: a five-digit reading ({widestReading:0.0}px) beside the bars would run into the controls at {controlsRect.Left}");
        Require(portrait.Right < bars[0].Left, $"{suffix}: the portrait touches the bars");
        for (int row = 0; row < 3; row++)
        {
            Segments control = controls.Control(row);
            Rectangle rect = Rect(control);
            Expect(measures, $"control {row}", rect, strip, 430 + 36, 4 + 33 * row + 2, InnerWidth - 430 - 36, 22);
            RequireJoined(suffix, control);
        }
        Console.WriteLine($"card layout {suffix}: overview {frame.Width}x{frame.Height} at {frame.Location}, {measures.Count} regions at the mock's positions, 12px rhythm title/strip/tiles, no overlap");
        });

        Step("inventory tile count", () =>
        {
            // The tile's own reading, the one it draws: occupied slots against the bag's size.
            var tileType = card.GetType().GetNestedType("StatusTile", BindingFlags.NonPublic)!;
            var reading = tileType.GetMethod("Reading", BindingFlags.Static | BindingFlags.NonPublic)!
                .Invoke(null, new object[] { card, live::AICompanion.Companion.ProfileCard.CardPage.Inventory })!;
            string count = (string)reading.GetType().GetField("Item2")!.GetValue(reading)!;
            int held = save.Bag.Count;
            Require(count == $"{held}/120", $"{suffix}: the Inventory tile must count against the bag's 120 slots; it reads '{count}'");
            Console.WriteLine($"inventory tile {suffix}: reads {count}");
        });
        Step("controls", () => VerifyNativeCard.Controls(controls));
        Step("title bar drag guard", () => VerifyNativeCard.TitleBarDragGuard(card, frameElement, title, suffix, dragBar: true));
        Step("inventory occlusion", () => VerifyNativeCard.InventoryOcclusion(card, owner, ui, graphics, target));

        // The occlusion check placed the card over the game's inventory; the rest starts from a card that has never moved.
        typeof(CardSystem).GetField("position", Private)!.SetValue(owner, null);
        Invoke(card, "ShowOverview");
        frame = Rect(frameElement);
        Render(graphics, batch, rasterizer, target, ui, size, output, $"Overview-{suffix}");
        Allocation("Overview");
        if (scale == 1f) Step("overview pixels", () => OverviewPixels(target, size, identity, suffix));
        if (scale == 1f) Step("no back on the overview", () => NoBackPixels(target, size, frame, (UIElement)Field(card, "close"), suffix));

        // ---- pages: the frame keeps its top-left, grows to the page height, and the title bar carries the page's actions ----
        Rectangle drawnPage = default;
        foreach (var (method, expected) in new[] { ("ShowInventory", new[] { "Loot All", "Deposit All", "Quick Stack", "Restock" }) })
        {
            Invoke(card, method);
            card.Update(new GameTime());
            var content = (UIElement)Field(card, "content");
            var bag = VerifyNativeCard.Descendants(card).OfType<Bag>().Single();
            drawnPage = Rect(frameElement);
            Step("inventory frame and actions", () =>
            {
                RequirePageFrame(suffix, frameElement, frame, view);
                RequireActions(suffix, card, title, expected, "Inventory");
                Expect(measures, "page content", Rect(content), Rect(frameElement), Inset, Inset + TitleHeight + Rhythm, InnerWidth, PageHeightAt(frameElement.GetDimensions().Y, view) - PageOverhead);
                RequireRhythm(suffix, "title bar", Rect(title), "page content", Rect(content));
                var back = (CardButton)Field(card, "back");
                Require(back.Parent == title && back.Text == "<", $"{suffix}: a page's title bar must carry the back button");
                Expect(measures, "back", Rect(back), Rect(frameElement), Inset, Inset, 30, 30);
            });
            Step("inventory layout", () => InventoryLayout(suffix, bag, Rect(content)));
            Step("inventory on screen", () =>
            {
                // The last bag row is only on the page once the grid is scrolled to its end, so it is measured there.
                var elements = TitleBarButtons(title).ToList();
                foreach (UIElement box in bag.Boxes)
                    elements.AddRange(box.Children.Select((slot, i) => ($"gear slot {i}", Rect(slot))));
                elements.Add(("bag scrollbar", Rect(bag.Scrollbar)));
                elements.Add(("first bag slot", Rect(bag.Grid.Children.First())));
                bag.Scrollbar.ViewPosition = float.MaxValue;
                bag.Update(new GameTime());
                try { elements.Add(("last bag slot at full scroll", Rect(bag.Grid.Children.Last()))); }
                finally { bag.Scrollbar.ViewPosition = 0; bag.Update(new GameTime()); }
                RequireOnScreen(suffix, "Inventory", view, elements);
            });
            Step("clipped bag slots are not drawn", () =>
            {
                DrawOnce(graphics, batch, rasterizer, target, ui);
                Rectangle viewport = Rect(bag.Viewport);
                int total = bag.Grid.Children.Count(), showing = bag.Grid.Children.Count(slot => Rect(slot).Intersects(viewport));
                Console.WriteLine($"bag slots drawn {suffix}: {bag.SlotsDrawn} of {total}, the {showing} inside the grid's viewport at the top of the grid");
                Require(showing < total, $"{suffix}: premise: some bag slots must lie outside the grid's viewport; all {total} are inside it");
                Require(bag.SlotsDrawn == showing, $"{suffix}: a frame drew {bag.SlotsDrawn} bag slots where {showing} lie inside the grid's viewport; a slot clipped away entirely must not be drawn");
            });
            Step("chest buttons", () => VerifyNativeCard.ChestButtons(card, bag));
            Step("page title bar drag guard", () => VerifyNativeCard.TitleBarDragGuard(card, frameElement, title, suffix, dragBar: false));
            Render(graphics, batch, rasterizer, target, ui, size, output, $"Inventory-{suffix}");
            Allocation("Inventory");
        }

        Invoke(card, "Show", live::AICompanion.Companion.ProfileCard.CardPage.MiningList);
        card.Update(new GameTime());
        var mining = VerifyNativeCard.Descendants(card).OfType<MiningPage>().Single();
        Step("mining list frame and actions", () =>
        {
            RequirePageFrame(suffix, frameElement, frame, view);
            RequireActions(suffix, card, title, new[] { "Skip marked", "Only marked" }, "Mining list");
        });
        Step("mining list layout", () => MiningLayout(suffix, mining, Rect((UIElement)Field(card, "content"))));
        Step("mining list on screen", () =>
        {
            var parts = mining.Children.ToArray();
            var scrollbar = (UIScrollbar)parts[1];
            var elements = TitleBarButtons(title).ToList();
            elements.Add(("ore grid", Rect(parts[0])));
            elements.Add(("ore scrollbar", Rect(scrollbar)));
            scrollbar.ViewPosition = float.MaxValue;
            try { elements.Add(("last ore at full scroll", mining.TileBounds(Preferences.Current.MiningList.Known.Count - 1))); }
            finally { scrollbar.ViewPosition = 0; }
            RequireOnScreen(suffix, "Mining list", view, elements);
        });
        Step("mining list page", () => VerifyNativeCard.MiningList(card, mining));
        Render(graphics, batch, rasterizer, target, ui, size, output, $"MiningList-{suffix}");
        Allocation("Mining list");
        if (scale == 1f) Step("mining list pixels", () => MiningPixels(target, size, mining, suffix));

        Invoke(card, "Show", live::AICompanion.Companion.ProfileCard.CardPage.Mastery);
        card.Update(new GameTime());
        var tree = VerifyNativeCard.Descendants(card).OfType<Mastery>().Single();
        Rectangle pageContent = Rect((UIElement)Field(card, "content"));
        Step("mastery frame and actions", () =>
        {
            RequirePageFrame(suffix, frameElement, frame, view);
            RequireActions(suffix, card, title, new[] { "-", "+", "Reset view" }, "Mastery");
        });
        Step("mastery unpicked", () => MasteryUnpicked(suffix, tree, pageContent));
        Render(graphics, batch, rasterizer, target, ui, size, output, $"Mastery-{suffix}");
        Allocation("Mastery");
        if (scale == 1f) Step("zoom glyphs", () => ZoomGlyphPixels(target, size, title, suffix));
        Step("off-canvas tree nodes are not drawn", () =>
        {
            CardButton Button(string label) => title.Children.OfType<CardButton>().Single(b => b.Text == label);
            // Zoomed all the way in about the canvas's centre, most of the tree lies outside the canvas.
            for (int i = 0; i < 12; i++) Button("+").LeftClick(new UIMouseEvent(Button("+"), Button("+").GetDimensions().Center()));
            try
            {
                DrawOnce(graphics, batch, rasterizer, target, ui);
                Rectangle canvas = Rect(tree.Canvas);
                float radius = Graph.NodeRadius * tree.Zoom;
                int inside = 0, touching = 0, shapes = Graph.Nodes.Length + 1;
                for (int i = 0; i < shapes; i++)
                {
                    bool centre = i == Graph.Nodes.Length;
                    Vector2 at = tree.Screen(centre ? Vector2.Zero : Graph.Nodes[i].Position);
                    float extent = centre || Graph.Nodes[i].Kind == Graph.NodeKind.Diamond ? radius * MathF.Sqrt(2) : radius;
                    if (at.X - extent >= canvas.Left && at.X + extent <= canvas.Right && at.Y - extent >= canvas.Top && at.Y + extent <= canvas.Bottom) inside++;
                    if (at.X + extent + 2 >= canvas.Left && at.X - extent - 2 <= canvas.Right && at.Y + extent + 2 >= canvas.Top && at.Y - extent - 2 <= canvas.Bottom) touching++;
                }
                Console.WriteLine($"tree nodes drawn {suffix}: {tree.NodesDrawn} of {shapes} at zoom {tree.Zoom:0.00}, with {inside} wholly inside the canvas and {touching} touching it");
                Require(touching < shapes, $"{suffix}: premise: zoomed in, some of the tree must lie outside the canvas; all {shapes} shapes touch it");
                Require(tree.NodesDrawn >= inside && tree.NodesDrawn <= touching,
                    $"{suffix}: a frame drew {tree.NodesDrawn} tree shapes where {inside} lie wholly inside the canvas and {touching} touch it; a shape wholly outside must not be drawn, and none inside may be skipped");
            }
            finally { Button("Reset view").LeftClick(new UIMouseEvent(Button("Reset view"), Button("Reset view").GetDimensions().Center())); }
        });
        Step("mastery interaction", () => VerifyNativeCard.MasteryInteraction(card, tree, pageContent));
        // Piercing, which the interaction left at level 2 of 5, picked with its panel open: a numbered five-segment bar and
        // the line for its third level.
        tree.Pick(3);
        card.Update(new GameTime());
        Step("mastery picked", () =>
        {
            MasteryPicked(suffix, tree, pageContent);
            string[] lines = Graph.Nodes[3].Content.LevelLines!;
            Require(Graph.Nodes[3].Content.Name == "Piercing" && tree.Level(3) == 2, $"{suffix}: premise: Piercing is picked at level 2; it is at {tree.Level(3)}");
            Require(Enumerable.Range(0, 5).Select(i => tree.LevelBar!.Label(i)).SequenceEqual(new[] { "1", "2", "3", "4", "5" }) && Enumerable.Range(0, 5).Count(tree.LevelBar!.IsSelected) == 2,
                $"{suffix}: Piercing's bar must be five numbered segments with two selected");
            Require(tree.EffectLine == lines[2], $"{suffix}: Piercing at level 2 must say its third level's line, '{lines[2]}'; it says '{tree.EffectLine}'");
            Console.WriteLine($"mastery picked {suffix}: Piercing at level 2 of 5, segments 1 to 5 with two selected, the panel says \"{tree.EffectLine}\"");        });
        Step("mastery on screen", () =>
        {
            var elements = TitleBarButtons(title).ToList();
            elements.Add(("graph", Rect(tree.Canvas)));
            elements.Add(("Learn", Rect(tree.LearnButton)));
            elements.AddRange(tree.LevelBar!.Segments.Select((segment, i) => ($"level segment {i + 1}", Rect(segment))));
            RequireOnScreen(suffix, "Mastery", view, elements);
        });
        Render(graphics, batch, rasterizer, target, ui, size, output, $"MasteryPicked-{suffix}");
        Allocation("Mastery picked");
        // The second weapon slot, a one-level node, picked: its level bar is one segment rounded at both ends.
        tree.Pick(2);
        card.Update(new GameTime());
        Render(graphics, batch, rasterizer, target, ui, size, output, $"MasteryOneLevel-{suffix}");
        Step("one-level node", () =>
        {
            Require(tree.LevelBar is { } one && one.Segments.Count == 1, $"{suffix}: a one-level node's level bar must have one segment");
            if (scale == 1f) LoneSegmentPixels(target, size, tree.LevelBar!, suffix);
        });
        // Core, the gold centre: picked by a click on it, refused before the tree is full, then two learns once it is full.
        Step("mastery core", () => VerifyNativeCard.MasteryCore(tree));
        // The Mastery tile reads the points spent, a step of its own so a failure inside the Core step cannot hide it.
        Step("mastery tile points", () =>
        {
            var tileType = card.GetType().GetNestedType("StatusTile", BindingFlags.NonPublic)!;
            var reading = tileType.GetMethod("Reading", BindingFlags.Static | BindingFlags.NonPublic)!
                .Invoke(null, new object[] { card, live::AICompanion.Companion.ProfileCard.CardPage.Mastery })!;
            string points = (string)reading.GetType().GetField("Item2")!.GetValue(reading)!;
            Require(points == "417 pts", $"{suffix}: the Mastery tile must read the points spent, 415 for the tree and 2 for Core; it reads '{points}'");
            Console.WriteLine($"mastery tile {suffix}: reads {points}");
        });
        card.Update(new GameTime());
        Render(graphics, batch, rasterizer, target, ui, size, output, $"MasteryCore-{suffix}");

        Invoke(card, "ShowOverview");
        Step("overview on screen", () =>
        {
            var elements = TitleBarButtons(title).ToList();
            elements.AddRange(footer.Children.Select((tile, i) => ($"tile {i}", Rect(tile))));
            for (int row = 0; row < 3; row++)
                elements.AddRange(controls.Control(row).Segments.Select((segment, i) => ($"control {row} segment {i}", Rect(segment))));
            RequireOnScreen(suffix, "Overview", view, elements);
        });
        Step("back to the overview", () =>
        {
            Require(Rect(frameElement).Location == frame.Location && Rect(frameElement).Height == OverviewHeight, $"{suffix}: back to the overview moved or resized the card");
            // The page frame the production card drew, not this file's formula for it, so a card that still runs past the edge fails.
            Console.WriteLine($"card pages {suffix}: Inventory, Mining list and Mastery keep the frame at {frame.Location} and are {drawnPage.Height} tall, ending {view.Y - drawnPage.Bottom:0} above the bottom edge of {view.Y:0}"
                + (belowMinimum ? $" (this viewport is below the game's minimum {minWidth}x{minHeight}, so the card's fixed 720 width is not required to fit)" : ""));
            Require(drawnPage.Height > 0 && drawnPage.Bottom <= view.Y + Tolerance, $"{suffix}: a page's foot runs {drawnPage.Bottom - view.Y:0}px past the bottom edge");
            if (!belowMinimum)
                Require(frame.X >= 0 && frame.Right <= view.X + Tolerance, $"{suffix}: at a resolution the game allows, the card must fit the screen's width");
        });
        ui.SetState(null);
        Step("escape closes only the card", VerifyEscapeClosesOnlyTheCard.Run);
        Step("shift-click with the Inventory page open", VerifyShiftClickFillsTheBag.Run);
        if (failures.Count > 0)
            throw new InvalidOperationException($"{failures.Count} card check(s) failed at {suffix}:\n" + string.Join("\n", failures));
    }

    // ---- layout helpers ----

    private static Rectangle Rect(UIElement element) => element.GetDimensions().ToRectangle();
    private static object Field(object owner, string name) => owner.GetType().GetField(name, Private)!.GetValue(owner)!;
    private static void Invoke(object owner, string name, params object[] args)
    {
        MethodInfo method = owner.GetType().GetMethods(Private | BindingFlags.Public).Single(m => m.Name == name && m.GetParameters().Length == args.Length);
        method.Invoke(owner, args);
    }

    /// <summary>A region at its expected position relative to a parent's top-left, and size.</summary>
    private static void Expect(List<string> measures, string name, Rectangle actual, Rectangle parent, float x, float y, float width, float height)
    {
        float dx = actual.X - parent.X, dy = actual.Y - parent.Y;
        Require(Math.Abs(dx - x) <= Tolerance && Math.Abs(dy - y) <= Tolerance && Math.Abs(actual.Width - width) <= Tolerance && Math.Abs(actual.Height - height) <= Tolerance,
            $"{name} is at ({dx},{dy}) {actual.Width}x{actual.Height}; the mock puts it at ({x},{y}) {width}x{height}");
        measures.Add(name);
    }

    private static void RequireRhythm(string suffix, string upper, Rectangle a, string lower, Rectangle b)
        => Require(Math.Abs(b.Top - a.Bottom - Rhythm) <= Tolerance, $"{suffix}: {upper} to {lower} is {b.Top - a.Bottom}px, not the {Rhythm}px rhythm");

    private static void RequireNoOverlap(string suffix, params (string Name, Rectangle Rect)[] regions)
    {
        for (int i = 0; i < regions.Length; i++)
            for (int k = i + 1; k < regions.Length; k++)
            {
                Rectangle overlap = Rectangle.Intersect(regions[i].Rect, regions[k].Rect);
                Require(overlap.Width <= 0 || overlap.Height <= 0, $"{suffix}: {regions[i].Name} and {regions[k].Name} overlap by {overlap}");
            }
    }

    /// <summary>A joined control's segments share their borders: the first starts on the control's left, the last ends on its right, neighbours overlap by the border.</summary>
    private static void RequireJoined(string suffix, Segments control)
    {
        Rectangle whole = Rect(control);
        Rectangle[] parts = control.Segments.Select(Rect).ToArray();
        Require(parts.Length > 0 && parts[0].Left == whole.Left && Math.Abs(parts[^1].Right - whole.Right) <= Tolerance && parts.All(p => p.Height == whole.Height),
            $"{suffix}: a joined control's segments must span it exactly: {whole} vs {string.Join(" ", parts)}");
        for (int i = 1; i < parts.Length; i++)
            Require(Math.Abs(parts[i - 1].Right - parts[i].Left - Segments.Border) <= Tolerance, $"{suffix}: segments {i - 1} and {i} must overlap by the border");
        for (int i = 0; i < parts.Length; i++)
            Require(Segments.Corners(i, parts.Length) == (parts.Length == 1 ? Primitives.AllCorners : i == 0 ? Primitives.LeftCorners : i == parts.Length - 1 ? Primitives.RightCorners : 0),
                $"{suffix}: segment {i} of {parts.Length} rounds the wrong corners");
    }

    private static void RequirePageFrame(string suffix, UIElement frameElement, Rectangle overview, Vector2 view)
    {
        Rectangle page = Rect(frameElement);
        CalculatedStyle drawn = frameElement.GetDimensions();
        float height = PageHeightAt(drawn.Y, view);
        Require(page.Location == overview.Location && page.Width == FrameWidth && Math.Abs(drawn.Height - height) <= Tolerance,
            $"{suffix}: a page must keep the card's top-left {overview.Location} and be {FrameWidth}x{height:0}; got {page}");
    }

    private static IEnumerable<(string Name, Rectangle Rect)> TitleBarButtons(UIElement title)
        => title.Children.OfType<CardButton>().Select(button => ($"title-bar {button.Text}", Rect(button)));

    /// <summary>
    /// Every interactive element of a page lies inside the viewport, measured against the screen rather than against the
    /// frame: a button placed correctly on its page is still unreachable below the bottom edge. Horizontally the check applies
    /// wherever the viewport is at least as wide as the card, because the card's width is fixed by the mock.
    /// </summary>
    private static void RequireOnScreen(string suffix, string page, Vector2 view, List<(string Name, Rectangle Rect)> elements)
    {
        bool widthFits = view.X >= FrameWidth;
        var off = elements.Where(e => e.Rect.Top < -Tolerance || e.Rect.Bottom > view.Y + Tolerance
            || (widthFits && (e.Rect.Left < -Tolerance || e.Rect.Right > view.X + Tolerance))).ToArray();
        Require(elements.Count > 0, $"{suffix}: premise: the {page} page must offer elements to measure");
        int lowest = elements.Max(e => e.Rect.Bottom);
        Console.WriteLine($"on screen {suffix} {page}: {elements.Count - off.Length} of {elements.Count} interactive elements inside the {view.X:0}x{view.Y:0} viewport, the lowest ending at {lowest}");
        Require(off.Length == 0, $"{suffix}: on the {page} page {off.Length} interactive element(s) are off the {view.X:0}x{view.Y:0} screen: "
            + string.Join("; ", off.Take(6).Select(e => $"{e.Name} at {e.Rect}")));
    }

    /// <summary>The page's actions in the title bar, in order, right-aligned to end one gap before the close button, 30 tall, clear of the title.</summary>
    private static void RequireActions(string suffix, UIState card, UIElement title, string[] labels, string pageTitle)
    {
        var close = (UIElement)Field(card, "close");
        var back = (UIElement)Field(card, "back");
        var buttons = title.Children.OfType<CardButton>().Where(b => b != close && b != back).OrderBy(b => Rect(b).X).ToArray();
        Require(buttons.Select(b => b.Text).SequenceEqual(labels), $"{suffix}: the title bar's actions are [{string.Join(", ", buttons.Select(b => b.Text))}], not [{string.Join(", ", labels)}]");
        Rectangle last = Rect(buttons[^1]);
        Require(Math.Abs(Rect(close).Left - last.Right - 8) <= Tolerance, $"{suffix}: the last action ends {Rect(close).Left - last.Right}px before the close button, not 8");
        for (int i = 0; i < buttons.Length; i++)
        {
            Rectangle r = Rect(buttons[i]);
            Require(r.Height == 30 && r.Y == Rect(title).Y, $"{suffix}: action {buttons[i].Text} must sit in the title bar at 30px tall");
            if (i > 0) Require(Math.Abs(r.Left - Rect(buttons[i - 1]).Right - 6) <= Tolerance, $"{suffix}: actions {buttons[i - 1].Text} and {buttons[i].Text} are not 6px apart");
            if (buttons[i].Text.Length > 1) Require(r.Width >= FontAssets.MouseText.Value.MeasureString(buttons[i].Text).X * .8f + 8, $"{suffix}: the {buttons[i].Text} pill is narrower than its label");
        }
        float titleRight = Rect(title).X + 42 + FontAssets.MouseText.Value.MeasureString(pageTitle).X;
        Require(Rect(buttons[0]).Left > titleRight, $"{suffix}: the first action overlaps the title '{pageTitle}' ending at {titleRight}");
    }

    private static void InventoryLayout(string suffix, Bag bag, Rectangle content)
    {
        var measures = new List<string>();
        Rectangle weapons = Rect(bag.Boxes[0]), tools = Rect(bag.Boxes[1]);
        // The mock's gear row spans what the bag spans: the grid's left edge to the scrollbar's right edge, 23 to 673.
        Expect(measures, "weapons box", weapons, content, 23, 0, 319, 64);
        Expect(measures, "tools box", tools, content, 354, 0, 319, 64);
        Rectangle gridBox = Rect(bag.Viewport), scrollBox = Rect(bag.Scrollbar);
        Require(weapons.Left == gridBox.Left && tools.Right == scrollBox.Right && tools.Left - weapons.Right == Rhythm && weapons.Width == tools.Width,
            $"{suffix}: the gear boxes must line up with the bag: weapons from the grid's left edge {gridBox.Left} (it starts at {weapons.Left}), tools to the scrollbar's right edge {scrollBox.Right} (it ends at {tools.Right}), equal and one rhythm apart");
        foreach (Rectangle box in new[] { weapons, tools })
        {
            UIElement element = bag.Boxes[box == weapons ? 0 : 1];
            Rectangle[] slots = element.Children.Select(Rect).OrderBy(r => r.X).ToArray();
            Require(slots.Length == 2 && slots.All(s => s.Width == 48 && s.Height == 48 && s.Y - box.Y == 8),
                $"{suffix}: a gear box must hold two fixed 48px slots centred top to bottom: {string.Join(" ", slots)}");
            int left = slots[0].Left - box.Left, middle = slots[1].Left - slots[0].Right, right = box.Right - slots[1].Right;
            Require(Math.Abs(left - middle) <= Tolerance && Math.Abs(middle - right) <= Tolerance, $"{suffix}: gear slots are not evenly spaced: {left}/{middle}/{right}");
        }
        Expect(measures, "bag grid", Rect(bag.Viewport), content, 23, 76, 620, content.Height - BagRowsAroundGrid);
        Expect(measures, "bag scrollbar", Rect(bag.Scrollbar), content, 653, 76, 20, content.Height - BagRowsAroundGrid);
        RequireRhythm(suffix, "gear boxes", weapons, "bag grid", Rect(bag.Viewport));
        Require(Math.Abs(content.Bottom - 24 - Rect(bag.Viewport).Bottom - Rhythm) <= Tolerance, $"{suffix}: the grid does not end one rhythm above the bottom line");
        Rectangle[] grid = bag.Grid.Children.Select(Rect).ToArray();
        Require(grid.Length == 120 && grid.All(r => r.Width == 48 && r.Height == 48), $"{suffix}: the bag must show its 120 fixed 48px slots; it shows {grid.Length}");
        int held = Main.LocalPlayer.GetModPlayer<live::AICompanion.Companion.PlayerIntegration.CompanionPlayer>().Bag.Count;
        Require(bag.CountLine == $"{held} / 120", $"{suffix}: the bottom line must count against the bag's 120 slots; it reads '{bag.CountLine}'");
        Require(grid.Count(r => r.Y == grid[0].Y) == 12 && grid[1].X - grid[0].X == 52, $"{suffix}: the bag grid must be 12 columns at a 52px pitch");
        RequireNoOverlap(suffix, ("weapons box", weapons), ("tools box", tools), ("bag grid", Rect(bag.Viewport)), ("bag scrollbar", Rect(bag.Scrollbar)));
        Console.WriteLine($"inventory layout {suffix}: two gear boxes {weapons.Width}x{weapons.Height} from the grid's left edge x {weapons.Left} to the scrollbar's right edge x {tools.Right}, evenly spaced slots, grid 620x{Rect(bag.Viewport).Height} twelve columns, 12px rhythm above and below the grid");
    }

    private static void MiningLayout(string suffix, MiningPage page, Rectangle content)
    {
        var measures = new List<string>();
        var parts = page.Children.ToArray();
        Expect(measures, "ore grid", Rect(parts[0]), content, 0, 0, 412, content.Height);
        Expect(measures, "ore scrollbar", Rect(parts[1]), content, 416, 0, 20, content.Height);
        Expect(measures, "ore preview", Rect(parts[2]), content, 456, 0, 240, Math.Min(214, content.Height));
        RequireNoOverlap(suffix, ("ore grid", Rect(parts[0])), ("ore scrollbar", Rect(parts[1])), ("ore preview", Rect(parts[2])));
        Console.WriteLine($"mining list layout {suffix}: grid 412 wide and scrollbar at the page's full {content.Height}px height, preview 240x{Rect(parts[2]).Height} under the mock's swatch, name and verdict");
    }

    private static void MasteryUnpicked(string suffix, Mastery tree, Rectangle content)
    {
        Require(tree.Picked == -1 && tree.Panel.Parent == null, $"{suffix}: the Mastery page must open with nothing picked and no panel");
        var measures = new List<string>();
        Expect(measures, "mastery graph", Rect(tree.Canvas), content, 0, 0, InnerWidth, content.Height);
        Rectangle canvas = Rect(tree.Canvas);
        var (min, max) = Mastery.TreeBounds();
        Vector2 centre = tree.Screen((min + max) / 2);
        Require(Vector2.Distance(centre, tree.Canvas.GetDimensions().Center()) <= 1.5f, $"{suffix}: Reset view does not centre the tree: its centre is at {centre}, the canvas's at {tree.Canvas.GetDimensions().Center()}");
        foreach (var node in Graph.Nodes)
            Require(canvas.Contains(tree.Screen(node.Position).ToPoint()), $"{suffix}: node {node.Content.Name} starts outside the fitted graph");
        for (int lane = 0; lane < Graph.Lanes.Length; lane++)
        {
            var (lo, hi) = Mastery.LabelBox(lane);
            Vector2 a = tree.Screen(lo), b = tree.Screen(hi);
            Require(a.X >= canvas.Left && a.Y >= canvas.Top && b.X <= canvas.Right && b.Y <= canvas.Bottom, $"{suffix}: the {Graph.Lanes[lane]} label runs out of the graph");
            // A label never sits on a node: the nearest point of the label's box to every node centre is farther than the node's reach.
            foreach (var node in Graph.Nodes)
            {
                Vector2 nearest = Vector2.Clamp(node.Position, lo, hi);
                float reach = node.Kind == Graph.NodeKind.Diamond ? Graph.NodeRadius * MathF.Sqrt(2) : Graph.NodeRadius;
                Require(Vector2.Distance(nearest, node.Position) > reach, $"{suffix}: the {Graph.Lanes[lane]} label sits on {node.Content.Name}");
            }
        }
        Console.WriteLine($"mastery unpicked {suffix}: the graph fills {canvas.Width}x{canvas.Height}, the tree and its four labels centred inside it, no label on a node, zoom {tree.Zoom:0.000}");
    }

    private static void MasteryPicked(string suffix, Mastery tree, Rectangle content)
    {
        var measures = new List<string>();
        Expect(measures, "narrowed graph", Rect(tree.Canvas), content, 0, 0, InnerWidth - 232, content.Height);
        Expect(measures, "detail panel", Rect(tree.Panel), content, InnerWidth - 220, 0, 220, content.Height);
        Rectangle panel = Rect(tree.Panel);
        Rectangle learn = Rect(tree.LearnButton), levels = Rect(tree.LevelBar!);
        Require(Math.Abs(levels.Bottom - (panel.Bottom - 12)) <= Tolerance && Math.Abs(levels.Top - learn.Bottom - 8) <= Tolerance && learn.Height == 34 && levels.Height == 22,
            $"{suffix}: Learn and the level bar must sit at the panel's very bottom: learn {learn}, levels {levels}, panel {panel}");
        RequireJoined(suffix, tree.LevelBar!);
        Require(Rect(tree.Canvas).Contains(tree.Screen(Graph.Nodes[tree.Picked].Position).ToPoint()), $"{suffix}: the picked node is covered by its own panel");
        Console.WriteLine($"mastery picked {suffix}: graph {Rect(tree.Canvas).Width} wide beside a 220px panel, Learn and a {tree.LevelBar!.Segments.Count}-segment level bar at its bottom");
    }

    // ---- rendering and pixels ----

    private static void Render(GraphicsDevice graphics, SpriteBatch batch, RasterizerState rasterizer, RenderTarget2D target, UserInterface ui, Point size, string output, string name)
    {
        graphics.SetRenderTarget(target); graphics.Clear(Background);
        batch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, DepthStencilState.None, rasterizer, null, Main.UIScaleMatrix);
        graphics.ScissorRectangle = new Rectangle(0, 0, size.X, size.Y);
        ui.Draw(batch, new GameTime()); batch.End();
        graphics.SetRenderTarget(null);
        string file = Path.Combine(output, name + ".png");
        using var stream = File.Create(file);
        target.SaveAsPng(stream, size.X, size.Y);
        Console.WriteLine("RENDER " + file);
    }

    /// <summary>
    /// Slack for the whole-frame subtraction below, in bytes per frame averaged over the measured frames. It is rounding, not
    /// room for an allocation: the smallest string a frame could build is larger than this.
    /// </summary>
    private const long OwnBytesPerFrameAllowed = 8;

    /// <summary>
    /// A page's steady frames split into the game font's share and the card's own. The glyphs the card hands the font over the
    /// measured frames are counted by <c>DrawCardPrimitives.GlyphsDrawn</c>; the font's cost per glyph is measured live on either
    /// side of the frames rather than written down, because it is a property of the font assembly and its compiled state, and
    /// the two readings must agree or the measurement is refused. Returns bytes per frame in all, glyphs per frame, bytes per
    /// glyph, and bytes per frame that are not the font's.
    /// </summary>
    internal static (long PerFrame, long GlyphsPerFrame, long PerGlyph, long OwnPerFrame) MeasureCardAllocation(GraphicsDevice graphics, SpriteBatch batch,
        RasterizerState rasterizer, RenderTarget2D target, UserInterface ui, int frames = 60)
    {
        var time = new GameTime();
        void Frame()
        {
            ui.Update(time);
            batch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, DepthStencilState.None, rasterizer, null, Main.UIScaleMatrix);
            ui.Draw(batch, time);
            batch.End();
        }
        graphics.SetRenderTarget(target);
        try
        {
            for (int i = 0; i < 30; i++) Frame();
            for (int attempt = 0; ; attempt++)
            {
                long glyphCostBefore = FontCostPerGlyph(batch, rasterizer);
                long glyphs = Primitives.GlyphsDrawn;
                long bytes = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < frames; i++) Frame();
                bytes = GC.GetAllocatedBytesForCurrentThread() - bytes;
                glyphs = Primitives.GlyphsDrawn - glyphs;
                long glyphCostAfter = FontCostPerGlyph(batch, rasterizer);
                if (glyphCostBefore == glyphCostAfter)
                    return (bytes / frames, glyphs / frames, glyphCostBefore, (bytes - glyphs * glyphCostBefore) / frames);
                if (attempt == 2)
                    throw new InvalidOperationException($"the font's cost per glyph changed during the measurement ({glyphCostBefore} then {glyphCostAfter} bytes) three times running, so the card's share cannot be separated");
            }
        }
        finally { graphics.SetRenderTarget(null); }
    }

    /// <summary>Bytes the mouse-text font allocates per glyph drawn, from a nine-glyph string drawn many times.</summary>
    private static long FontCostPerGlyph(SpriteBatch batch, RasterizerState rasterizer)
    {
        var font = FontAssets.MouseText.Value;
        const string word = "Companion";
        batch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, DepthStencilState.None, rasterizer, null, Main.UIScaleMatrix);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 20; i++) batch.DrawString(font, word, new Vector2(10, 10), Color.White, 0, Vector2.Zero, .8f, SpriteEffects.None, 0);
        long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        batch.End();
        return bytes / (20 * word.Length);
    }

    /// <summary>
    /// The same measurement split by who allocates: bytes per frame of the interface's update, and of each element's own
    /// drawing (its subtree less its children's subtrees), the largest first. This names the source of whatever a page still
    /// allocates, so a residue is attributed rather than argued.
    /// </summary>
    internal static string AllocationByElement(GraphicsDevice graphics, SpriteBatch batch, RasterizerState rasterizer, RenderTarget2D target, UserInterface ui, UIState card, int frames = 20)
    {
        var time = new GameTime();
        long Measure(Action action)
        {
            void Once()
            {
                batch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, DepthStencilState.None, rasterizer, null, Main.UIScaleMatrix);
                action();
                batch.End();
            }
            for (int i = 0; i < 5; i++) Once();
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < frames; i++) Once();
            return (GC.GetAllocatedBytesForCurrentThread() - before) / frames;
        }
        var own = new List<(string Name, long Bytes)>();
        var ownUpdate = new List<(string Name, long Bytes)>();
        static string NameOf(UIElement element) => element is CardButton text ? $"{element.GetType().Name}('{text.Text}')" : element.GetType().Name;
        long Walk(UIElement element, bool update)
        {
            long whole = Measure(update ? () => element.Update(time) : () => element.Draw(batch));
            long children = 0;
            foreach (UIElement child in element.Children.ToArray()) children += Walk(child, update);
            (update ? ownUpdate : own).Add((NameOf(element), whole - children));
            return whole;
        }
        static string Top(List<(string Name, long Bytes)> list) => string.Join(", ", list.Where(e => e.Bytes > 0).GroupBy(e => e.Name)
            .Select(g => (Name: $"{g.Key}{(g.Count() > 1 ? $" x{g.Count()}" : "")}", Bytes: g.Sum(e => e.Bytes))).OrderByDescending(e => e.Bytes).Take(8).Select(e => $"{e.Name} {e.Bytes}"));
        graphics.SetRenderTarget(target);
        try
        {
            long update = Measure(() => ui.Update(time));
            Walk(card, update: true);
            long draw = Walk(card, update: false);
            return $"update {update} (by element: {Top(ownUpdate)}), draw {draw}; by element: {Top(own)}";
        }
        finally { graphics.SetRenderTarget(null); }
    }

    /// <summary>
    /// What the game's own text drawing allocates per call, measured with nothing of the card involved: the mouse-text font's
    /// <c>DrawString</c> and <c>MeasureString</c> on a nine-character string, and <c>Utils.DrawBorderString</c>, which every
    /// <c>UITextPanel</c> draws its label through, on that string and on an empty one. Whatever the card allocates beyond
    /// these per drawn string is its own.
    /// </summary>
    internal static string GameTextCost(GraphicsDevice graphics, SpriteBatch batch, RasterizerState rasterizer, RenderTarget2D target)
    {
        var font = FontAssets.MouseText.Value;
        long PerCall(Action action)
        {
            batch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, DepthStencilState.None, rasterizer, null, Main.UIScaleMatrix);
            for (int i = 0; i < 10; i++) action();
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 100; i++) action();
            long bytes = (GC.GetAllocatedBytesForCurrentThread() - before) / 100;
            batch.End();
            return bytes;
        }
        graphics.SetRenderTarget(target);
        try
        {
            const string word = "Companion";
            long draw = PerCall(() => batch.DrawString(font, word, new Vector2(10, 10), Color.White, 0, Vector2.Zero, .8f, SpriteEffects.None, 0));
            long measure = PerCall(() => font.MeasureString(word));
            long border = PerCall(() => Utils.DrawBorderString(batch, word, new Vector2(10, 10), Color.White, .8f));
            long empty = PerCall(() => Utils.DrawBorderString(batch, "", new Vector2(10, 10), Color.White, .8f));
            return $"font DrawString('{word}') {draw} bytes a call, MeasureString {measure}, Utils.DrawBorderString('{word}') {border}, Utils.DrawBorderString('') {empty}";
        }
        finally { graphics.SetRenderTarget(null); }
    }

    /// <summary>One frame of the interface drawn into the target and not saved, for a check that reads what the frame did.</summary>
    private static void DrawOnce(GraphicsDevice graphics, SpriteBatch batch, RasterizerState rasterizer, RenderTarget2D target, UserInterface ui)
    {
        graphics.SetRenderTarget(target);
        batch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, DepthStencilState.None, rasterizer, null, Main.UIScaleMatrix);
        ui.Draw(batch, new GameTime());
        batch.End();
        graphics.SetRenderTarget(null);
    }

    private static Color[] Pixels(RenderTarget2D target, Point size)
    {
        var pixels = new Color[size.X * size.Y];
        target.GetData(pixels);
        return pixels;
    }

    private static void OverviewPixels(RenderTarget2D target, Point size, Status identity, string suffix)
    {
        Color[] pixels = Pixels(target, size);
        Color At(int x, int y) => pixels[y * size.X + x];
        Rectangle portrait = Status.Portrait(Rect(identity));
        Color core = Portrait.CoreColour;
        int corePixels = 0;
        for (int y = portrait.Top; y < portrait.Bottom; y++)
            for (int x = portrait.Left; x < portrait.Right; x++)
                if (Math.Abs(At(x, y).R - core.R) <= 6 && Math.Abs(At(x, y).G - core.G) <= 6 && Math.Abs(At(x, y).B - core.B) <= 6) corePixels++;
        Require(corePixels >= 9, $"{suffix}: the drone's core was not drawn in the portrait ({corePixels} core pixels)");
        foreach (var row in Enumerable.Range(0, 3))
        {
            Rectangle icon = Rect(identity.Controls.Control(row).Parent.Children.First());
            Color panel = At(icon.Left, icon.Top);
            int painted = 0;
            for (int y = icon.Top; y < icon.Bottom; y++)
                for (int x = icon.Left; x < icon.Right; x++)
                    if (Math.Abs(At(x, y).R - panel.R) + Math.Abs(At(x, y).G - panel.G) + Math.Abs(At(x, y).B - panel.B) > 60) painted++;
            Require(painted >= 20, $"{suffix}: the icon of control row {row} was not painted ({painted} pixels differ from the panel)");
        }
        Console.WriteLine($"overview pixels {suffix}: the drone's core drawn ({corePixels} px), all three control icons painted");
    }

    private static void MiningPixels(RenderTarget2D target, Point size, MiningPage page, string suffix)
    {
        Color[] pixels = Pixels(target, size);
        var list = Preferences.Current.MiningList;
        double Luminance(int index)
        {
            Rectangle tile = page.TileBounds(index);
            double sum = 0; int n = 0;
            for (int y = tile.Top + 10; y < tile.Top + 38; y++)
                for (int x = tile.Left + 10; x < tile.Left + 38; x++)
                { Color c = pixels[y * size.X + x]; sum += .299 * c.R + .587 * c.G + .114 * c.B; n++; }
            return sum / n;
        }
        int dim = list.Known.ToList().IndexOf(TileID.Iron), bright = list.Known.ToList().IndexOf(TileID.Silver);
        Require(dim >= 0 && bright >= 0 && !list.Allows(TileID.Iron) && list.Allows(TileID.Silver), "premise: iron is left and silver is mined in the render");
        double dimLight = Luminance(dim), brightLight = Luminance(bright);
        Require(brightLight > dimLight + 20, $"{suffix}: a left ore must be drawn dimmer than a mined one; iron {dimLight:0.0} against silver {brightLight:0.0}");
        Console.WriteLine($"mining list pixels {suffix}: the left iron swatch reads {dimLight:0.0} against the mined silver's {brightLight:0.0}");
    }

    /// <summary>
    /// The overview's title bar has no back button: at the place a page draws it, not one pixel of the button's border
    /// colour or its hover gold. The close button at the other end, drawn the same way, is the premise that the check can
    /// see a button at all.
    /// </summary>
    private static void NoBackPixels(RenderTarget2D target, Point size, Rectangle frame, UIElement close, string suffix)
    {
        Color[] pixels = Pixels(target, size);
        int Border(Rectangle r)
        {
            int count = 0;
            for (int y = Math.Max(0, r.Top); y < Math.Min(size.Y, r.Bottom); y++)
                for (int x = Math.Max(0, r.Left); x < Math.Min(size.X, r.Right); x++)
                {
                    Color c = pixels[y * size.X + x];
                    bool edge = Math.Abs(c.R - Primitives.Edge.R) <= 12 && Math.Abs(c.G - Primitives.Edge.G) <= 12 && Math.Abs(c.B - Primitives.Edge.B) <= 12;
                    bool gold = c.R > 220 && c.G > 190 && c.B < 90;
                    if (edge || gold) count++;
                }
            return count;
        }
        var backPlace = new Rectangle(frame.X + Inset, frame.Y + Inset, 30, 30);
        int backBorder = Border(backPlace);
        Require(backBorder == 0, $"{suffix}: something is drawn where a page's back button sits on the overview: {backBorder} button-coloured pixels in {backPlace}");
        // On a screen narrower than the card, close is past the right edge and cannot be the premise here; the wider viewports read it.
        if (!new Rectangle(0, 0, size.X, size.Y).Contains(Rect(close)))
        {
            Console.WriteLine($"no back on the overview {suffix}: 0 button pixels at {backPlace}; close is past the screen's edge at {Rect(close)}, so the premise is read at the wider viewports");
            return;
        }
        int closeBorder = Border(Rect(close));
        Require(closeBorder >= 20, $"{suffix}: premise: the close button's border must be visible to this check ({closeBorder} pixels)");
        Console.WriteLine($"no back on the overview {suffix}: 0 button pixels at {backPlace}, against {closeBorder} on close");
    }

    /// <summary>
    /// Zoom in and zoom out read as two symbols: "+" has ink in its centre column above and below its middle, "-" has ink
    /// across its middle and none above or below. At UI scale 1 the game font had drawn both as the same dash, which
    /// passes the premise here and fails the stroke.
    /// </summary>
    private static void ZoomGlyphPixels(RenderTarget2D target, Point size, UIElement title, string suffix)
    {
        Color[] pixels = Pixels(target, size);
        int Ink(Rectangle r, int fromY, int toY)
        {
            int count = 0;
            for (int y = r.Center.Y + fromY; y <= r.Center.Y + toY; y++)
                for (int x = r.Center.X - 1; x <= r.Center.X + 1; x++)
                    if (x >= 0 && y >= 0 && x < size.X && y < size.Y && pixels[y * size.X + x] is { R: > 200, G: > 200, B: > 200 }) count++;
            return count;
        }
        var buttons = title.Children.OfType<CardButton>().ToArray();
        Rectangle plus = Rect(buttons.Single(b => b.Text == "+")), minus = Rect(buttons.Single(b => b.Text == "-"));
        int plusStroke = Ink(plus, -6, -3) + Ink(plus, 3, 6), minusStroke = Ink(minus, -6, -3) + Ink(minus, 3, 6), minusBar = Ink(minus, -1, 1);
        Require(minusBar >= 3, $"{suffix}: premise: the - button must draw ink across its middle ({minusBar} pixels)");
        Require(plusStroke >= 8 && minusStroke == 0, $"{suffix}: zoom in and zoom out must be different symbols; + has {plusStroke} ink pixels above and below its middle, - has {minusStroke}");
        Console.WriteLine($"zoom glyphs {suffix}: + has {plusStroke} ink pixels above and below its middle, - has none there and {minusBar} across it");
    }

    /// <summary>
    /// A lone level segment is rounded at both ends: its top corner pixels are not border, while the middle of its top edge
    /// is. The border is gold while the level is learned and the edge colour while it is not, so the colour looked for is
    /// the one this segment is drawn with.
    /// </summary>
    private static void LoneSegmentPixels(RenderTarget2D target, Point size, Segments bar, string suffix)
    {
        Color[] pixels = Pixels(target, size);
        Rectangle r = Rect(bar.Segments[0]);
        Require(r.Top >= 0 && r.Bottom <= size.Y, $"{suffix}: premise: the lone level segment must be on screen top to bottom to be read; it is at {r}");
        if (r.Left < 0 || r.Right > size.X)
        {
            // Only a screen narrower than the card's fixed 720 width puts it past a side edge; the wider viewports read it.
            Console.WriteLine($"lone level segment {suffix}: past the side of a screen narrower than the card at {r}, so its pixels are read at the wider viewports");
            return;
        }
        Color border = bar.IsSelected(0) ? Color.Gold : Primitives.Edge;
        bool Border(int x, int y) { Color c = pixels[y * size.X + x]; return Math.Abs(c.R - border.R) <= 12 && Math.Abs(c.G - border.G) <= 12 && Math.Abs(c.B - border.B) <= 12; }
        Require(Border(r.Center.X, r.Top), $"{suffix}: premise: the lone segment's top edge must be border-coloured at its middle");
        Require(!Border(r.Left, r.Top) && !Border(r.Right - 1, r.Top), $"{suffix}: a lone level segment must be rounded at both ends; its top-left or top-right corner pixel is square");
        Console.WriteLine($"lone level segment {suffix}: rounded at both ends");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
