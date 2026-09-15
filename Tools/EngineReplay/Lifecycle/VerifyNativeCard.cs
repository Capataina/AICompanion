extern alias live;
using System.Reflection;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent.UI.Elements;
using Terraria.ID;
using Terraria.UI;
using Graph = live::AICompanion.Companion.ProfileCard.DefineMasteryGraph;
using Mastery = live::AICompanion.Companion.ProfileCard.PreviewMasteryTree;
using Controls = live::AICompanion.Companion.ProfileCard.ControlWorkPreferences;
using MiningPage = live::AICompanion.Companion.ProfileCard.ShowMiningList;
using Bag = live::AICompanion.Companion.Inventory.CompanionBagUI;
using Preferences = live::AICompanion.Companion.PlayerIntegration.CompanionPreferences;
using ListMode = live::AICompanion.Companion.PlayerIntegration.MiningListMode;
using WorkPolicies = live::AICompanion.Companion.Brain.Activities.WorkPolicies;
using WorkPolicy = live::AICompanion.Companion.Brain.Activities.WorkPolicy;

/// <summary>
/// Drives the production card's own controls and pages, never a second layout model: the work preferences through
/// their segments, the title bar's drag guard, the chest buttons against real inventories, the covered game inventory,
/// the mining list's marks and mode, and the mastery page's picking, panning and learning. <see cref="RunMasteryRules"/>
/// needs no graphics and runs in the default suite; the rest runs inside the offscreen renderer.
/// </summary>
internal static class VerifyNativeCard
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static object Field(object owner, string name) => owner.GetType().GetField(name, Private)!.GetValue(owner)!;
    private static void Call(object owner, string name) => owner.GetType().GetMethods(Private | BindingFlags.Public).First(m => m.Name == name && m.GetParameters().Length == 0).Invoke(owner, null);
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Click(UIElement element) => element.LeftClick(new UIMouseEvent(element, element.GetDimensions().Center()));
    internal static IEnumerable<UIElement> Descendants(UIElement parent)
    {
        foreach (UIElement child in parent.Children)
        { yield return child; foreach (UIElement nested in Descendants(child)) yield return nested; }
    }

    /// <summary>Every segment of the three controls changes the live preference the brain reads.</summary>
    public static void Controls(Controls controls)
    {
        var mining = WorkPolicies.Mining;
        var chopping = WorkPolicies.Chopping;
        bool torches = Preferences.Current.TorchPlacement;
        try
        {
            for (int row = 0; row < 2; row++)
                for (int choice = 0; choice < 3; choice++)
                {
                    Click(controls.Control(row).Segments[choice]);
                    WorkPolicy read = row == 0 ? WorkPolicies.Mining : WorkPolicies.Chopping;
                    Require((int)read == choice && controls.Control(row).IsSelected(choice), $"control row {row} segment {choice} did not set the live work policy (read {read})");
                }
            Click(controls.Control(2).Segments[0]);
            Require(!Preferences.Current.TorchPlacement, "the torches Off segment did not turn torch placement off");
            Click(controls.Control(2).Segments[1]);
            Require(Preferences.Current.TorchPlacement, "the torches On segment did not turn torch placement on");
        }
        finally { WorkPolicies.Mining = mining; WorkPolicies.Chopping = chopping; Preferences.Current.TorchPlacement = torches; }
        Console.WriteLine("card controls: every mining and chopping segment sets the live policy, torches Off and On set placement");
    }

    /// <summary>A press on a title-bar button never drags the card; a press on the bar itself does.</summary>
    public static void TitleBarDragGuard(UIState card, UIElement frame, UIElement title, string suffix, bool dragBar)
    {
        Rectangle before = frame.GetDimensions().ToRectangle();
        var close = (UIElement)Field(card, "close");
        var back = (UIElement)Field(card, "back");
        // Every button the title bar carries right now: close alone on the overview, and on a page back and the page's
        // actions too.
        UIElement[] buttons = title.Children.OfType<UITextPanel<string>>().ToArray<UIElement>();
        Require(buttons.Contains(close) && buttons.Contains(back) == (back.Parent == title), $"{suffix}: premise: the drag guard presses every title-bar button");
        foreach (UIElement button in buttons)
        {
            Vector2 press = button.GetDimensions().Center();
            Main.mouseX = (int)press.X; Main.mouseY = (int)press.Y; Main.mouseLeft = true;
            title.LeftMouseDown(new UIMouseEvent(button, press));
            Main.mouseX += 40; Main.mouseY += 25; card.Update(new GameTime());
            Main.mouseLeft = false; title.LeftMouseUp(new UIMouseEvent(button, Main.MouseScreen)); card.Update(new GameTime());
            Require(frame.GetDimensions().ToRectangle().Location == before.Location, $"{suffix}: a press on a title-bar button dragged the card");
        }
        if (!dragBar)
        {
            Main.mouseX = Main.mouseY = -100;
            Console.WriteLine($"title bar {suffix}: presses on {buttons.Length} page title-bar buttons, back included, never drag");
            return;
        }
        Vector2 bar = title.GetDimensions().Position() + new Vector2(300, 10);
        Main.mouseX = (int)bar.X; Main.mouseY = (int)bar.Y; Main.mouseLeft = true;
        title.LeftMouseDown(new UIMouseEvent(title, bar));
        Main.mouseX += 18; Main.mouseY += 7; card.Update(new GameTime());
        Main.mouseLeft = false; title.LeftMouseUp(new UIMouseEvent(title, Main.MouseScreen)); card.Update(new GameTime());
        Rectangle moved = frame.GetDimensions().ToRectangle();
        Require(moved.X != before.X || moved.Y != before.Y, $"{suffix}: dragging the title bar itself did not move the card");
        Main.mouseX = Main.mouseY = -100;
        Console.WriteLine($"title bar {suffix}: a press on close never drags, a press on the bar moves the card {moved.X - before.X},{moved.Y - before.Y}");
    }

    /// <summary>
    /// The chest buttons, pressed through the real title-bar buttons against prepared inventories: Loot All conserves items
    /// into a full, part-full and empty player inventory; Deposit All takes the main inventory below the hotbar and keeps
    /// favourites; Quick Stack moves only kinds the bag holds; Restock tops up the player's partial stacks only.
    /// </summary>
    public static void ChestButtons(UIState card, Bag page)
    {
        var title = (UIElement)Field(card, "titleBar");
        UITextPanel<string> Button(string label) => title.Children.OfType<UITextPanel<string>>().Single(b => b.Text == label);
        Player player = Main.LocalPlayer;
        var bag = player.GetModPlayer<live::AICompanion.Companion.PlayerIntegration.CompanionPlayer>().Bag;
        Item[] savedBag = bag.Items.Select(item => item.Clone()).ToArray();
        Item[] savedPlayer = player.inventory.Select(item => item.Clone()).ToArray();
        bool dedicated = Main.dedServ, itemText = Main.showItemText;
        int Total(Item[] items) => items.Where(item => !item.IsAir).Sum(item => item.stack);
        int Of(Item[] items, int type) => items.Where(item => item.type == type).Sum(item => item.stack);
        void Clear(Item[] items) { for (int i = 0; i < items.Length; i++) items[i] = new Item(); }
        void Put(Item[] items, int slot, int type, int stack, bool favourite = false)
        { items[slot] = new Item(); items[slot].SetDefaults(type); items[slot].stack = stack; items[slot].favorited = favourite; }
        try
        {
            Main.dedServ = true; // Native insertion remains real; this fixture owns no sound device.
            // The game's Loot All settings show a pickup popup, which reads item names and popup slots this service shell never
            // loaded. PopupText.NewText returns first thing when the player has item text switched off, so the fixture switches
            // it off for the presses and back after; the transfers themselves stay the game's.
            Main.showItemText = false;

            // Loot All: full, partial and empty player inventories conserve every item.
            for (int i = 0; i < 50; i++) Put(player.inventory, i, ItemID.DirtBlock, 9999);
            int bagBefore = Total(bag.Items);
            Click(Button("Loot All"));
            Require(Total(bag.Items) == bagBefore, "Loot All into a full player inventory lost bag items");
            player.inventory[0] = new Item();
            Click(Button("Loot All"));
            int took = Total(player.inventory) - 49 * 9999;
            Require(took > 0 && Total(bag.Items) + took == bagBefore, "Loot All into one free slot lost items or used no slot");
            Clear(player.inventory);
            int remaining = Total(bag.Items);
            Click(Button("Loot All"));
            Require(bag.Count == 0 && Total(player.inventory) == remaining && page.Message.Length > 0, "Loot All into an empty inventory did not move every item, or said nothing");

            // Deposit All: the hotbar and favourites stay, coins stay with the player, everything else below the hotbar goes.
            Clear(bag.Items); Clear(player.inventory);
            Put(player.inventory, 0, ItemID.DirtBlock, 5);
            Put(player.inventory, 12, ItemID.StoneBlock, 7, favourite: true);
            Put(player.inventory, 13, ItemID.Gel, 9);
            Put(player.inventory, 14, ItemID.CopperCoin, 30);
            Put(player.inventory, 15, ItemID.Wood, 40);
            Click(Button("Deposit All"));
            Require(Of(player.inventory, ItemID.DirtBlock) == 5 && Of(player.inventory, ItemID.StoneBlock) == 7 && Of(player.inventory, ItemID.CopperCoin) == 30,
                "Deposit All must keep the hotbar, favourites and coins with the player");
            Require(Of(bag.Items, ItemID.Gel) == 9 && Of(bag.Items, ItemID.Wood) == 40 && Of(player.inventory, ItemID.Gel) == 0 && Of(player.inventory, ItemID.Wood) == 0,
                "Deposit All must move the gel and wood below the hotbar into the bag");

            // Quick Stack: only kinds the bag already holds move.
            Put(player.inventory, 20, ItemID.Wood, 25);
            Put(player.inventory, 21, ItemID.Torch, 3);
            Click(Button("Quick Stack"));
            Require(Of(bag.Items, ItemID.Wood) == 65 && Of(player.inventory, ItemID.Wood) == 0 && Of(player.inventory, ItemID.Torch) == 3 && Of(bag.Items, ItemID.Torch) == 0,
                "Quick Stack must move the wood the bag holds and leave the torches it does not");

            // Restock: the player's partial gel stack is topped up from the bag in its own slot; wood the player does not carry
            // stays in the bag. Gel is ammo, and Restock also swaps ammo into an empty ammo slot, so a total alone passes
            // with partial stacks never topped up at all: the stack must grow where it is, and no other slot may fill.
            Clear(player.inventory);
            Put(player.inventory, 3, ItemID.Gel, 1);
            int gelTotal = Of(bag.Items, ItemID.Gel) + 1;
            Click(Button("Restock"));
            int filled = player.inventory.Count(item => !item.IsAir);
            Require(player.inventory[3].type == ItemID.Gel && player.inventory[3].stack == gelTotal && filled == 1 && Of(bag.Items, ItemID.Gel) == 0 && Of(bag.Items, ItemID.Wood) == 65,
                $"Restock must top up the player's gel in its own slot from the bag and leave the wood; slot 3 holds {player.inventory[3].stack} of {gelTotal}, {filled} slot(s) filled, bag wood {Of(bag.Items, ItemID.Wood)}");
        }
        finally
        {
            Main.dedServ = dedicated;
            Main.showItemText = itemText;
            for (int i = 0; i < bag.Items.Length; i++) bag.Items[i] = savedBag[i];
            for (int i = 0; i < savedPlayer.Length; i++) player.inventory[i] = savedPlayer[i];
            page.GetType().GetField("message", Private)!.SetValue(page, "");
            page.Recalculate();
        }
        Console.WriteLine("chest buttons: Loot All conserves into full, partial and empty inventories; Deposit All keeps hotbar, favourites and coins; Quick Stack moves only bag kinds; Restock tops up partial stacks");
    }

    /// <summary>
    /// A covered game inventory slot never takes the press the card receives, on every page; the visible bag slot takes it
    /// once; an uncovered game slot still works. The card is placed over the game's inventory for the check, because a card
    /// at its default place under the notch covers none of it.
    /// </summary>
    public static void InventoryOcclusion(UIState card, live::AICompanion.Companion.ProfileCard.CompanionProfileCardSystem owner, UserInterface ui, GraphicsDevice graphics, RenderTarget2D target)
    {
        bool inventoryWasOpen = Main.playerInventory;
        Call(owner, "Close");
        Main.playerInventory = false; Call(card, "ShowInventory");
        Require(Main.playerInventory, "The card did not enter native inventory mode");
        Call(owner, "Close"); Require(!Main.playerInventory, "Closing the card left its player inventory open");
        Main.playerInventory = true; Call(card, "ShowInventory");
        Call(owner, "Close"); Require(Main.playerInventory, "Closing the card closed an inventory it did not open");
        Main.playerInventory = inventoryWasOpen;
        var ownerType = owner.GetType();
        ownerType.GetField("position", Private)!.SetValue(owner, (Vector2?)Vector2.Zero);
        var open = ownerType.GetProperty("IsOpen", BindingFlags.Public | BindingFlags.Static)!;
        bool wasOpen = (bool)open.GetValue(null)!;
        var player = Main.LocalPlayer;
        var bag = player.GetModPlayer<live::AICompanion.Companion.PlayerIntegration.CompanionPlayer>().Bag;
        Item savedBag = bag.Items[0].Clone(), savedCursor = Main.mouseItem.Clone();
        Item[] savedPlayer = player.inventory.Select(item => item.Clone()).ToArray();
        bool left = Main.mouseLeft, release = Main.mouseLeftRelease, dedicated = Main.dedServ;
        Terraria.GameInput.PlayerInput.SetZoom_Unscaled();
        Point savedPointer = new(Main.mouseX, Main.mouseY);
        Terraria.GameInput.PlayerInput.SetZoom_UI();
        ownerType.GetField("state", Private)!.SetValue(owner, card);
        ownerType.GetField("ui", Private)!.SetValue(owner, ui);
        ownerType.GetField("lastTime", Private)!.SetValue(owner, new GameTime());
        open.SetValue(null, true);
        int hits = 0, playerSlot = 0;
        Rectangle nativeSlot = default;
        void Pointer(Point point)
        {
            Main.mouseX = (int)Math.Ceiling(point.X * Main.UIScale);
            Main.mouseY = (int)Math.Ceiling(point.Y * Main.UIScale);
            Terraria.GameInput.PlayerInput.CacheMousePositionForZoom();
            Terraria.GameInput.PlayerInput.SetZoom_UI();
        }
        var layers = new List<GameInterfaceLayer>
        {
            new LegacyGameInterfaceLayer("Vanilla: Inventory", () =>
            {
                // The native main-inventory loop's pointer predicate, followed by its real slot handler.
                if (nativeSlot.Contains(Main.MouseScreen.ToPoint()) && !Terraria.GameInput.PlayerInput.IgnoreMouseInterface)
                { hits++; ItemSlot.LeftClick(player.inventory, ItemSlot.Context.InventoryItem, playerSlot); }
                return true;
            }, InterfaceScaleType.UI),
            new LegacyGameInterfaceLayer("Vanilla: Mouse Text", () => true, InterfaceScaleType.UI)
        };
        owner.ModifyInterfaceLayers(layers);
        Require(layers[0] is live::AICompanion.Companion.ProfileCard.BlockCoveredInventoryInput, "The native inventory layer has no card occlusion guard");
        try
        {
            Main.dedServ = true;
            Call(card, "ShowInventory"); card.Update(new GameTime());
            var inventory = Descendants(card).OfType<Bag>().Single();
            Rectangle visible = inventory.Grid.Children.First().GetDimensions().ToRectangle();
            Rectangle overlap = default;
            // Main.DrawInventory uses a 56px pitch at inventoryScale=.85.
            for (int i = 0; i < 50; i++)
            {
                var slot = new Rectangle((int)(20 + (i % 10) * 56 * .85f), (int)(20 + (i / 10) * 56 * .85f), 44, 44);
                Rectangle intersection = Rectangle.Intersect(slot, visible);
                if (intersection.Width <= 2 || intersection.Height <= 2) continue;
                nativeSlot = slot; playerSlot = i; overlap = intersection; break;
            }
            Require(overlap.Width > 0, $"The input fixture did not reach an overlapping pair of slots: first bag slot {visible}");
            foreach (string page in new[] { "ShowInventory", "ShowOverview", "ShowMastery" })
            {
                bag.Items[0] = savedBag.Clone();
                player.inventory[playerSlot].SetDefaults(ItemID.DirtBlock); player.inventory[playerSlot].stack = 5;
                Main.mouseItem.TurnToAir(); Main.mouseLeft = Main.mouseLeftRelease = true; hits = 0;
                if (page == "ShowMastery") card.GetType().GetMethod("Show", Private)!.Invoke(card, new object[] { live::AICompanion.Companion.ProfileCard.CardPage.Mastery });
                else Call(card, page);
                card.Update(new GameTime()); Pointer(overlap.Center);
                graphics.SetRenderTarget(target); graphics.Clear(Color.Transparent);
                graphics.ScissorRectangle = new Rectangle(0, 0, target.Width, target.Height);
                foreach (var layer in layers) Require(layer.Draw(), "An interface layer failed while testing occlusion");
                Require(hits == 0 && player.inventory[playerSlot].stack == 5, $"A card click reached the obscured player slot on {page}");
                Require(Main.MouseScreen.ToPoint() == overlap.Center, "The inventory guard did not restore UI pointer coordinates");
                if (page == "ShowInventory")
                    Require(bag.Items[0].IsAir && Main.mouseItem.type == savedBag.type && Main.mouseItem.stack == savedBag.stack,
                        "A covered native slot blocked or double-handled the visible companion slot");
                else Require(Main.mouseItem.IsAir, "A profile or mastery click lifted an obscured item");
            }
            // An uncovered game slot far right of the card, which starts at the screen's left edge and is 720 wide.
            nativeSlot = new Rectangle(760, 20, 44, 44); playerSlot = 0;
            player.inventory[0].SetDefaults(ItemID.DirtBlock); player.inventory[0].stack = 5;
            Main.mouseItem.TurnToAir(); hits = 0; Main.mouseLeft = Main.mouseLeftRelease = true; Pointer(new Point(762, 22));
            if (!((UIElement)Field(card, "frame")).ContainsPoint(Main.MouseScreen))
            {
                foreach (var layer in layers) Require(layer.Draw(), "An interface layer failed for uncovered inventory");
                Require(hits == 1 && player.inventory[0].IsAir && Main.mouseItem.type == ItemID.DirtBlock, "The card blocked the uncovered player inventory");
            }
            Console.WriteLine("interface-layer input: covered slots receive no press on every page, visible bag receives one, uncovered inventory works, pointer and owned inventory mode restored");
        }
        finally
        {
            graphics.SetRenderTarget(null);
            bag.Items[0] = savedBag; Main.mouseItem = savedCursor;
            for (int i = 0; i < savedPlayer.Length; i++) player.inventory[i] = savedPlayer[i];
            Main.mouseLeft = left; Main.mouseLeftRelease = release; Main.dedServ = dedicated;
            Main.mouseX = savedPointer.X; Main.mouseY = savedPointer.Y;
            Terraria.GameInput.PlayerInput.CacheMousePositionForZoom(); Terraria.GameInput.PlayerInput.SetZoom_UI();
            ownerType.GetField("state", Private)!.SetValue(owner, null);
            ownerType.GetField("ui", Private)!.SetValue(owner, null);
            open.SetValue(null, wasOpen);
        }
    }

    /// <summary>The page lists exactly the known ores; a click flips a mark in the list the brain reads; the mode buttons set the mode.</summary>
    public static void MiningList(UIState card, MiningPage page)
    {
        var list = Preferences.Current.MiningList;
        var title = (UIElement)Field(card, "titleBar");
        UITextPanel<string> Button(string label) => title.Children.OfType<UITextPanel<string>>().Single(b => b.Text == label);
        Require(list.Known.Count > 8, "premise: the seeded list must be longer than one row");
        Require(!list.Known.Contains(TileID.Adamantite), "premise: an ore the player never held is not known");
        CalculatedStyle viewport = page.Children.First().GetDimensions();
        Require(page.TileBounds(0).Location == new Point((int)viewport.X, (int)viewport.Y) && page.TileBounds(8).Y == page.TileBounds(0).Y + 52,
            "the ore grid must start at the page's top-left with eight columns at a 52px pitch");
        int silver = list.Known.ToList().IndexOf(TileID.Silver);
        Require(list.Allows(TileID.Silver), "premise: silver is unmarked and mined under Skip marked");
        Main.mouseX = page.TileBounds(silver).Center.X; Main.mouseY = page.TileBounds(silver).Center.Y;
        page.LeftClick(new UIMouseEvent(page, page.TileBounds(silver).Center.ToVector2()));
        Require(list.IsMarked(TileID.Silver) && !list.Allows(TileID.Silver) && page.Shown == silver, "a click on silver must mark it in the live list and show it in the preview");
        Click(Button("Only marked"));
        Require(list.Mode == ListMode.OnlyMarked && list.Allows(TileID.Silver) && !list.Allows(TileID.Copper), "the Only marked button must make only marked ores work");
        Click(Button("Skip marked"));
        page.LeftClick(new UIMouseEvent(page, page.TileBounds(silver).Center.ToVector2()));
        Require(list.Mode == ListMode.SkipMarked && !list.IsMarked(TileID.Silver) && list.Allows(TileID.Silver), "Skip marked and a second click must restore silver as mined");
        Main.mouseX = Main.mouseY = -100;
        Console.WriteLine($"mining list page: {list.Known.Count} known ores listed and no others, a click marks the live list, the mode buttons set the mode");
    }

    /// <summary>
    /// Picking a node opens the panel without moving the view; a click on empty background unpicks; a drag pans and never
    /// unpicks; Learn refuses a node whose needs are unlearned even when its parent is learned, and takes a level once they are.
    /// </summary>
    public static void MasteryInteraction(UIState card, Mastery tree, Rectangle content)
    {
        var title = (UIElement)Field(card, "titleBar");
        UITextPanel<string> Button(string label) => title.Children.OfType<UITextPanel<string>>().Single(b => b.Text == label);
        float fitted = tree.Zoom;
        Click(Button("+")); Require(tree.Zoom > fitted, "the + button did not zoom in");
        Click(Button("-")); Click(Button("-")); Require(tree.Zoom < fitted, "the - button did not zoom out");
        Click(Button("Reset view")); Require(Math.Abs(tree.Zoom - fitted) < 1e-4f, "Reset view did not restore the fitted view");

        void Press(Vector2 at, Vector2 release)
        {
            Main.mouseX = (int)at.X; Main.mouseY = (int)at.Y; Main.mouseLeft = true;
            tree.Canvas.LeftMouseDown(new UIMouseEvent(tree.Canvas, at));
            Main.mouseX = (int)release.X; Main.mouseY = (int)release.Y; tree.Update(new GameTime());
            Main.mouseLeft = false; tree.Canvas.LeftMouseUp(new UIMouseEvent(tree.Canvas, release)); tree.Update(new GameTime());
        }
        Vector2 origin = tree.Origin;
        float zoom = tree.Zoom;
        Vector2 node = tree.Screen(Graph.Nodes[0].Position);
        Press(node, node);
        Require(tree.Picked == 0 && tree.Panel.Parent != null, "a click on Combat's first node must pick it and open the panel");
        Require(tree.Zoom == zoom && tree.Origin == origin, "opening the panel must keep the view's left edge and scale when the node is not covered");
        CalculatedStyle canvas = tree.Canvas.GetDimensions();
        Vector2 empty = new(canvas.X + 6, canvas.Y + 6);
        Require(tree.NodeAt(empty) == -1, "premise: the graph's top-left corner is empty background");
        Press(empty, empty + new Vector2(30, 20));
        Require(tree.Picked == 0 && tree.Origin == origin + new Vector2(30, 20), "a drag must pan the view and keep the picked node");
        Press(empty, empty + new Vector2(3, 2));
        Require(tree.Picked == -1 && tree.Panel.Parent == null && tree.Canvas.GetDimensions().Width == content.Width,
            "a click on empty background, under the drag threshold, must unpick and close the panel");
        Click(Button("Reset view"));

        // Third weapon slot (role 5) opens from Piercing (role 3) and needs Second weapon slot (role 2).
        const int attackSpeed = 1, secondSlot = 2, piercing = 3, thirdSlot = 5;
        tree.Pick(0); Click(tree.LearnButton);
        tree.Pick(attackSpeed); Click(tree.LearnButton);
        tree.Pick(piercing); Click(tree.LearnButton);
        Require(tree.Level(0) == 1 && tree.Level(attackSpeed) == 1 && tree.Level(piercing) == 1, "premise: Damage, Attack speed and Piercing learn along their edges");
        tree.Pick(thirdSlot); Click(tree.LearnButton);
        Require(tree.Level(thirdSlot) == 0 && !tree.CanLearn(thirdSlot), "Learn must refuse the third weapon slot while the second is unlearned, although Piercing into it is learned");
        tree.Pick(secondSlot); Click(tree.LearnButton);
        tree.Pick(thirdSlot); Click(tree.LearnButton);
        Require(tree.Level(thirdSlot) == 1, "once its need is learned, the third weapon slot must take a level");
        tree.Pick(0); Click(tree.LearnButton);
        Require(tree.Level(0) == 2 && tree.LevelBar!.Segments.Count == 5 && Enumerable.Range(0, 5).Count(i => tree.LevelBar.IsSelected(i)) == 2,
            "Damage, a circle, must show five level segments with its two learned levels selected");
        Require(!tree.LevelBar.Segments.Any(segment => segment.GetType().GetField("OnLeftClick", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(segment) != null),
            "the level bar is a display and must take no clicks");
        MasteryLevelLines(tree);
        Main.mouseX = Main.mouseY = -100;
        Console.WriteLine("mastery interaction: zoom and reset, pick keeps the view, background click unpicks, drag pans, Learn follows edges and needs");
    }

    /// <summary>
    /// A node whose levels step unevenly says one line at a time: the next level's line, the last once full, and a hovered
    /// segment's own line until the pointer leaves it, which the segments are told through their real mouse-over and
    /// mouse-out. An even node's line never changes under hover. Five-level bars are numbered and three-level bars say
    /// "Level N". Leaves Piercing at level 2 of 5 for the render.
    /// </summary>
    private static void MasteryLevelLines(Mastery tree)
    {
        const int piercing = 3, extraProjectile = 4, secondSlot = 2, damage = 0;
        int bagSpace = Array.FindIndex(Graph.Nodes, n => n.Content.Name == "Bag space");
        string[] Lines(int node) => Graph.Nodes[node].Content.LevelLines!;
        void Hover(int segment) { UIElement s = tree.LevelBar!.Segments[segment]; s.MouseOver(new UIMouseEvent(s, s.GetDimensions().Center())); }
        void Leave(int segment) { UIElement s = tree.LevelBar!.Segments[segment]; s.MouseOut(new UIMouseEvent(s, s.GetDimensions().Center())); }
        string[] Labels() => Enumerable.Range(0, tree.LevelBar!.Segments.Count).Select(tree.LevelBar.Label).ToArray();

        tree.Pick(piercing);
        Require(tree.Level(piercing) == 1 && Lines(piercing).Length == 5, "premise: Piercing is at level 1 of 5");
        Require(Labels().SequenceEqual(new[] { "1", "2", "3", "4", "5" }), $"a five-level bar must be numbered 1 to 5; it reads [{string.Join(", ", Labels())}]");
        Require(tree.EffectLine == Lines(piercing)[1], $"Piercing at level 1 must say its second level's line; it says '{tree.EffectLine}'");
        foreach (int segment in new[] { 0, 4 })
        {
            Hover(segment);
            Require(tree.EffectLine == Lines(piercing)[segment], $"hovering Piercing's segment {segment + 1} must say that level's line '{Lines(piercing)[segment]}'; it says '{tree.EffectLine}'");
            Leave(segment);
            Require(tree.EffectLine == Lines(piercing)[1], $"leaving Piercing's segment {segment + 1} must restore the next level's line; it says '{tree.EffectLine}'");
        }
        Click(tree.LearnButton);
        Require(tree.Level(piercing) == 2 && tree.EffectLine == Lines(piercing)[2], $"Piercing at level 2 must say its third level's line; it says '{tree.EffectLine}'");

        tree.Pick(extraProjectile);
        for (int i = 0; i < 5; i++) Click(tree.LearnButton);
        Require(tree.Level(extraProjectile) == 5 && !tree.CanLearn(extraProjectile), "premise: Extra projectile learns to its fifth and last level");
        Require(tree.EffectLine == Lines(extraProjectile)[4], $"a full uneven node must say its last line; Extra projectile says '{tree.EffectLine}'");

        tree.Pick(bagSpace);
        Require(Labels().SequenceEqual(new[] { "Level 1", "Level 2", "Level 3" }), $"a three-level bar must say Level 1 to Level 3; Bag space reads [{string.Join(", ", Labels())}]");
        Require(tree.Level(bagSpace) == 0 && tree.EffectLine == Lines(bagSpace)[0], $"unlearned Bag space must say its first line; it says '{tree.EffectLine}'");

        tree.Pick(secondSlot);
        Require(Labels().SequenceEqual(new[] { "Level 1" }), $"a weapon slot's bar must be one segment, Level 1; it reads [{string.Join(", ", Labels())}]");

        tree.Pick(damage);
        string effect = Graph.Nodes[damage].Content.Effect;
        Hover(1);
        Require(tree.EffectLine == effect, $"hovering an even node's segment must leave its per-level effect; Damage says '{tree.EffectLine}'");
        Leave(1);
        Require(tree.EffectLine == effect, "an even node must say its per-level effect");

        tree.Pick(piercing);
        Console.WriteLine($"mastery level lines: Piercing says each next level's line and a hovered segment's own, a full Extra projectile says its last, five-level bars are numbered and Bag space's three say Level N");
    }

    /// <summary>The flat tree's structure and learning rule, with no graphics: the default suite runs this.</summary>
    public static int RunMasteryRules()
    {
        Require(Graph.Lanes.SequenceEqual(new[] { "Combat", "Movement", "Survival", "Gathering" }), "the four lanes, Combat at the top and then clockwise");
        Require(Graph.Roles == 10 && Graph.Nodes.Length == 4 * 10 + 4, $"four ten-node lanes and four shared circles; got {Graph.Nodes.Length} nodes");
        Require(Graph.DiamondRoles.SequenceEqual(new[] { 2, 5, 8 }), "diamonds at the left of the first split rank, the right of the second, and the meeting");
        for (int i = 0; i < Graph.FirstShared; i++)
            Require((Graph.Nodes[i].Kind == Graph.NodeKind.Diamond) == Graph.DiamondRoles.Contains(i % Graph.Roles), $"node {i} is not the kind its role says");

        // The owner's rank rule: a levelling circle has five levels, a levelling diamond three, a weapon slot one. An uneven
        // node carries exactly one line per level, and only the three the owner named step unevenly.
        foreach (var node in Graph.Nodes)
        {
            int ranks = Graph.IsWeaponSlot(node) ? 1 : node.Kind == Graph.NodeKind.Diamond ? 3 : 5;
            Require(node.Content.Levels == ranks, $"{node.Content.Name} takes {node.Content.Levels} levels; a {(Graph.IsWeaponSlot(node) ? "weapon slot" : node.Kind.ToString().ToLowerInvariant())} takes {ranks}");
            if (node.Content.LevelLines is { } lines)
                Require(lines.Length == node.Content.Levels && lines.All(line => line.Length > 0), $"{node.Content.Name} has {lines.Length} level lines for {node.Content.Levels} levels");
        }
        var uneven = Graph.Nodes.Where(n => n.Content.LevelLines != null).Select(n => n.Content.Name).ToArray();
        Require(uneven.SequenceEqual(new[] { "Piercing", "Extra projectile", "Bag space" }), $"the uneven nodes are Piercing, Extra projectile and Bag space; got [{string.Join(", ", uneven)}]");
        int compared = RequireTheMocksNodes();
        Require(Graph.Edges.Length == 4 * 11 + 4 * 2, $"eleven edges per lane and two into each shared circle; got {Graph.Edges.Length}");
        for (int j = 0; j < 4; j++)
        {
            int shared = Graph.FirstShared + j;
            var incoming = Graph.Edges.Where(e => e.To == shared).Select(e => e.From).OrderBy(f => f).ToArray();
            var expected = new[] { j * 10 + 2, (j + 1) % 4 * 10 + 3 }.OrderBy(f => f).ToArray();
            Require(incoming.SequenceEqual(expected) && !Graph.Edges.Any(e => e.From == shared), $"shared circle {j} must open from lane {j}'s role 2 and lane {(j + 1) % 4}'s role 3 and lead nowhere");
        }
        Require(Graph.Nodes[5].Needs.SequenceEqual(new[] { 2 }) && Graph.Nodes[8].Needs.SequenceEqual(new[] { 2, 5 }), "the third weapon slot needs the second, the fourth needs both");
        Require(Graph.NearestPair >= 2 * Graph.NodeRadius, $"no two nodes may touch: nearest pair {Graph.NearestPair} against a {2 * Graph.NodeRadius}-unit node");
        Require(Graph.Nodes.Select(n => n.Content.Name).All(name => name.Length > 0) && Graph.Nodes.All(n => n.Content.Effect.Length > 0 && n.Content.Levels >= 1),
            "every node carries a name, an effect and at least one level");
        var reachable = new HashSet<int> { -1 };
        for (int pass = 0; pass < Graph.Nodes.Length; pass++)
            foreach (var edge in Graph.Edges)
                if (reachable.Contains(edge.From)) reachable.Add(edge.To);
        Require(reachable.Count == Graph.Nodes.Length + 1, "every node must connect to the centre");

        // The rule itself, held without constructing the page, because the page's buttons measure text through fonts the
        // default suite does not load.
        var levels = new int[Graph.Nodes.Length];
        Require(Graph.CanLearn(levels, 0) && !Graph.CanLearn(levels, 1), "only a lane's first node opens from the centre");
        levels[1] = 1; levels[3] = 1;
        Require(!Graph.CanLearn(levels, 5), "a node with an unlearned need must refuse a level even when an edge into it is learned");
        levels[2] = 1;
        Require(Graph.CanLearn(levels, 5), "a node whose edge and needs are learned must take a level");
        levels[0] = Graph.Nodes[0].Content.Levels;
        Require(!Graph.CanLearn(levels, 0), "a node at its last level takes no more");
        Array.Clear(levels);
        levels[2] = 1;
        Require(Graph.CanLearn(levels, Graph.FirstShared), "a shared circle opens from either neighbouring lane's first-rank node");
        Array.Clear(levels);
        levels[(1 + 1) * Graph.Roles + 3] = 1;
        Require(Graph.CanLearn(levels, Graph.FirstShared + 1), "a shared circle also opens from the next lane's other first-rank node");
        Console.WriteLine($"mastery rules: {Graph.Nodes.Length} nodes, {Graph.Edges.Length} edges, nearest pair {Graph.NearestPair:0} units against {2 * Graph.NodeRadius}, "
            + $"circles 5 levels, levelling diamonds 3, weapon slots 1, {uneven.Length} uneven nodes with a line per level, {compared} nodes equal to the mock's LANES and JUNCTIONS, edges and needs govern learning");
        return 0;
    }

    /// <summary>
    /// The port is the mock's, read from the mock itself: every node call in <c>LANES</c> then <c>JUNCTIONS</c>, in order,
    /// must match the native node at that index in name, level count (C five, D three, A one), effect, needs and, for an
    /// <c>L(...)</c> node, its lines. Returns how many nodes were compared.
    /// </summary>
    private static int RequireTheMocksNodes()
    {
        string? mock = null;
        foreach (string start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
            for (var dir = new DirectoryInfo(start); dir != null && mock == null; dir = dir.Parent)
            {
                string candidate = Path.Combine(dir.FullName, "InterfaceExperiments", "companion-card.html");
                if (File.Exists(candidate)) mock = candidate;
            }
        Require(mock != null, "the agreed mock InterfaceExperiments/companion-card.html must be found above the working directory");
        string html = File.ReadAllText(mock!);
        int from = html.IndexOf("const LANES = [", StringComparison.Ordinal), to = html.IndexOf("const COLORS", StringComparison.Ordinal);
        Require(from >= 0 && to > from, "the mock must carry LANES and JUNCTIONS before COLORS");
        string data = html[from..to];
        const string quoted = @"'((?:[^'\\]|\\.)*)'";
        const string argument = @"(?:'(?:[^'\\]|\\.)*'|[A-Z_]+)";
        string Unescape(string s) => s.Replace("\\'", "'");
        var calls = System.Text.RegularExpressions.Regex.Matches(data,
            $@"\b([CDA])\({quoted},\s*{quoted},\s*{argument},\s*{argument}(?:,\s*\[([\d,\s]*)\])?\)");
        var lineBlocks = System.Text.RegularExpressions.Regex.Matches(data, $@"\),\s*\[((?:\s*{quoted},?)+)\s*\]\)");
        Require(calls.Count == Graph.Nodes.Length, $"the mock has {calls.Count} nodes and the port {Graph.Nodes.Length}");
        for (int i = 0; i < calls.Count; i++)
        {
            var call = calls[i];
            var content = Graph.Nodes[i].Content;
            int ranks = call.Groups[1].Value switch { "C" => 5, "D" => 3, _ => 1 };
            int[] needs = call.Groups[4].Success ? call.Groups[4].Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(int.Parse).ToArray() : Array.Empty<int>();
            Require(content.Name == Unescape(call.Groups[2].Value) && content.Effect == Unescape(call.Groups[3].Value) && content.Levels == ranks && content.Needs.SequenceEqual(needs),
                $"node {i} is '{content.Name}' ({content.Levels} levels, needs [{string.Join(",", content.Needs)}], '{content.Effect}') where the mock has '{Unescape(call.Groups[2].Value)}' ({ranks}, needs [{string.Join(",", needs)}], '{Unescape(call.Groups[3].Value)}')");
            // A line block belongs to the node call it follows, before the next call starts.
            int end = i + 1 < calls.Count ? calls[i + 1].Index : data.Length;
            var block = lineBlocks.Cast<System.Text.RegularExpressions.Match>().FirstOrDefault(m => m.Index > call.Index && m.Index < end);
            string[]? lines = block == null ? null
                : System.Text.RegularExpressions.Regex.Matches(block.Groups[1].Value, quoted).Select(m => Unescape(m.Groups[1].Value)).ToArray();
            Require((lines == null) == (content.LevelLines == null) && (lines == null || lines.SequenceEqual(content.LevelLines!)),
                $"{content.Name}'s level lines differ from the mock's: [{string.Join(" | ", content.LevelLines ?? Array.Empty<string>())}] against [{string.Join(" | ", lines ?? Array.Empty<string>())}]");
        }
        return calls.Count;
    }
}
