extern alias live;
using System.Reflection;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent.UI.Elements;
using Terraria.UI;
using GraphData = live::AICompanion.Companion.ProfileCard.DefineMasteryGraph;
using Mastery = live::AICompanion.Companion.ProfileCard.PreviewMasteryTree;

/// <summary>Exercises the production UI's events and layout, not a second layout model.</summary>
internal static class VerifyNativeCard
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static object Field(object owner, string name) => owner.GetType().GetField(name, Private)!.GetValue(owner)!;
    private static void Call(object owner, string name) => owner.GetType().GetMethod(name, Private | BindingFlags.Public)!.Invoke(owner, null);
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Click(UIElement element) => element.LeftClick(new UIMouseEvent(element, element.GetDimensions().Center()));
    internal static IEnumerable<UIElement> Descendants(UIElement parent)
    {
        foreach (UIElement child in parent.Children)
        { yield return child; foreach (UIElement nested in Descendants(child)) yield return nested; }
    }

    public static void VerifyNavigation(UIState card)
    {
        var frame = (UIElement)Field(card, "frame");
        var title = (UIElement)Field(card, "titleBar");
        Rectangle original = frame.GetDimensions().ToRectangle();
        float expectedWidth = Math.Min(780, Terraria.GameInput.PlayerInput.OriginalScreenSize.X / Main.UIScale - 24);
        Require(Math.Abs(original.Width - expectedWidth) < 1, $"UI scale was applied twice: expected width {expectedWidth}, got {original.Width}");
        Vector2 press = title.GetDimensions().Position() + new Vector2(150, 10);
        Main.mouseX = (int)press.X; Main.mouseY = (int)press.Y; Main.mouseLeft = true;
        title.LeftMouseDown(new UIMouseEvent(title, press));
        Main.mouseX += 18; Main.mouseY += 7; card.Update(new GameTime());
        Main.mouseLeft = false; title.LeftMouseUp(new UIMouseEvent(title, Main.MouseScreen));
        Rectangle moved = frame.GetDimensions().ToRectangle();
        Require(moved.X != original.X || moved.Y != original.Y, "Dragging the title bar did not move the card");
        int expectedX = Math.Min(original.X + 18, Main.screenWidth - original.Width);
        Require(Math.Abs(moved.X - expectedX) <= 1, "Title drag displacement differs from the UI mouse displacement");
        var tiles = ((UIElement)Field(card, "footer")).Children.ToArray();
        Require(tiles.Length == 2, "The card must have exactly two persistent navigation tiles");
        foreach (UIElement tile in tiles)
        {
            Click(tile); card.Recalculate();
            Require(frame.GetDimensions().ToRectangle() == moved, "A bottom tile changed the card position or size");
            Require(((UIElement)Field(card, "identity")).Parent == frame, "The action explanation disappeared on a subpage");
            Require(ReferenceEquals(tiles[0], ((UIElement)Field(card, "footer")).Children.First()), "Page navigation recreated the status strip");
        }
        Click((UIElement)Field(card, "back"));
        Require((string)Field(card, "page") == "Companion", "Back did not return to the profile");
        Require(!Descendants(title).OfType<UITextPanel<string>>().Any(b => b.Text is "Profile" or "Inventory" or "Mastery" or "Cargo"), "Duplicate navigation survives in the title bar");
        var minimise = Descendants(title).OfType<UITextPanel<string>>().Single(b => b.Text == "_");
        Click(minimise); Require(frame.GetDimensions().Height == 54, "Minimise did not collapse to the title bar");
        Click(minimise); Require(frame.GetDimensions().ToRectangle() == moved, "Restore changed the panel frame");
        Main.mouseX = Main.mouseY = -100;
        Console.WriteLine("PASS native card navigation: title drag, two persistent tiles, back, minimise and restore preserve the panel");
    }

    public static void VerifyPage(UIState card, string page)
    {
        Rectangle frame = ((UIElement)Field(card, "frame")).GetDimensions().ToRectangle();
        Require(new Rectangle(0, 0, Main.screenWidth, Main.screenHeight).Contains(frame), "Card extends outside its screen");
        Rectangle content = ((UIElement)Field(card, "content")).GetDimensions().ToRectangle();
        Rectangle footer = ((UIElement)Field(card, "footer")).GetDimensions().ToRectangle();
        Require(content.Bottom < footer.Top, "Page body overlaps the permanent tiles");
        if (page == "ShowOverview")
        {
            var body = (UIElement)Field(card, "content");
            var list = body.Children.OfType<UIList>().Single();
            var note = body.Children.Last();
            Require(list.OverflowHidden && list.GetDimensions().ToRectangle().Bottom + 8 <= note.GetDimensions().Y,
                "Scrolling behaviour rows touch the fixed explanatory note");
            Console.WriteLine("PASS behaviour list: clipped rows leave a clear gap before the fixed note");
        }
        if (page == "ShowInventory")
        {
            var bag = Descendants(card).OfType<live::AICompanion.Companion.Inventory.CompanionBagUI>().Single();
            bag.GetType().GetField("inspected", Private)!.SetValue(bag, 0);
            UIElement grid = (UIElement)Field(bag, "grid");
            Require(grid.Children.Count() == 100, "All filter must expose all one hundred storage slots");
            var positions = grid.Children.Select(slot => slot.GetDimensions().ToRectangle()).ToArray();
            Require(positions.All(r => r.Width == 48 && r.Height == 48), "Inventory slots stretched with panel width");
            Require(positions[1].X - positions[0].X == 52, "Inventory pitch differs from fixed slot plus gap");
            var buttons = Descendants(bag).OfType<UITextPanel<string>>().ToArray();
            Click(buttons.Single(b => b.Text == "Ore"));
            Require(grid.Children.Count() == 21, "Ore filter did not retain the original ore slot indices");
            Click(buttons.Single(b => b.Text == "Wood"));
            Require(grid.Children.Count() == 21, "Wood filter did not retain the original wood slot indices");
            Click(buttons.Single(b => b.Text == "Loot"));
            Require(grid.Children.Count() == 20, "Loot filter must exclude ore and wood");
            Click(buttons.Single(b => b.Text == "All")); bag.Update(new GameTime());
            VerifyTransfers(bag, buttons.Single(b => b.Text == "Hand everything over"));
            Console.WriteLine($"PASS native inventory: 100 fixed 48px slots; {positions.Count(r => r.Y == positions[0].Y)} columns; Ore/Wood/Loot filters preserve slots");
        }
        if (page == "ShowMastery") VerifyCanvas(Descendants(card).OfType<Mastery>().Single());
    }

    private static void VerifyTransfers(live::AICompanion.Companion.Inventory.CompanionBagUI page, UIElement handOver)
    {
        var bag = Main.LocalPlayer.GetModPlayer<live::AICompanion.Companion.PlayerIntegration.CompanionPlayer>().Bag;
        Item[] savedBag = bag.Items.Select(item => item.Clone()).ToArray();
        Item[] savedPlayer = Main.LocalPlayer.inventory.Select(item => item.Clone()).ToArray();
        bool dedicated = Main.dedServ;
        try
        {
            Main.dedServ = true; // Native insertion remains real; this fixture owns no sound device.
            for (int i = 0; i < 50; i++) { Main.LocalPlayer.inventory[i].SetDefaults(Terraria.ID.ItemID.DirtBlock); Main.LocalPlayer.inventory[i].stack = Main.LocalPlayer.inventory[i].maxStack; }
            int total = bag.Items.Sum(item => item.stack);
            Click(handOver);
            Require(bag.Items.Sum(item => item.stack) == total, "A full player inventory lost bag items during hand-over");
            Main.LocalPlayer.inventory[0].TurnToAir();
            Click(handOver);
            int moved = Main.LocalPlayer.inventory[0].stack;
            Require(moved > 0 && bag.Items.Sum(item => item.stack) + moved == total, "Partial hand-over lost items or failed to use the available slot");
            for (int i = 0; i < 50; i++) Main.LocalPlayer.inventory[i].TurnToAir();
            int remaining = bag.Items.Sum(item => item.stack);
            Click(handOver);
            Require(bag.Count == 0 && Main.LocalPlayer.inventory.Take(50).Sum(item => item.stack) == remaining, "Hand-over did not preserve every item in an empty player inventory");
        }
        finally
        {
            Main.dedServ = dedicated;
            for (int i = 0; i < bag.Items.Length; i++) bag.Items[i] = savedBag[i];
            for (int i = 0; i < savedPlayer.Length; i++) Main.LocalPlayer.inventory[i] = savedPlayer[i];
            page.GetType().GetField("transferMessage", Private)!.SetValue(page, "");
            page.Recalculate();
        }
        Console.WriteLine("PASS native hand-over: full, partial and empty player inventories preserve item totals");
    }

    public static void VerifyInventoryOcclusion(UIState card, live::AICompanion.Companion.ProfileCard.CompanionProfileCardSystem owner, UserInterface ui, GraphicsDevice graphics, RenderTarget2D target)
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
                // The native main-inventory loop's pointer predicate, followed by
                // its real slot handler. This probe needs no unrelated inventory art.
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
            var inventory = Descendants(card).OfType<live::AICompanion.Companion.Inventory.CompanionBagUI>().Single();
            Rectangle visible = ((UIElement)Field(inventory, "grid")).Children.First().GetDimensions().ToRectangle();
            Rectangle overlap = default;
            // Main.DrawInventory uses a 56px pitch at inventoryScale=.85.
            for (int i = 0; i < 50; i++)
            {
                var slot = new Rectangle((int)(20 + (i % 10) * 56 * .85f), (int)(20 + (i / 10) * 56 * .85f), 44, 44);
                Rectangle intersection = Rectangle.Intersect(slot, visible);
                if (intersection.Width <= 2 || intersection.Height <= 2) continue;
                nativeSlot = slot; playerSlot = i; overlap = intersection; break;
            }
            Require(overlap.Width > 0, "The input fixture did not reach an overlapping pair of slots");
            foreach (string page in new[] { "ShowInventory", "ShowOverview", "ShowMastery" })
            {
                bag.Items[0] = savedBag.Clone();
                player.inventory[playerSlot].SetDefaults(Terraria.ID.ItemID.DirtBlock); player.inventory[playerSlot].stack = 5;
                Main.mouseItem.TurnToAir(); Main.mouseLeft = Main.mouseLeftRelease = true; hits = 0;
                Call(card, page); card.Update(new GameTime()); Pointer(overlap.Center);
                graphics.SetRenderTarget(target); graphics.Clear(Color.Transparent);
                graphics.ScissorRectangle = new Rectangle(0, 0, target.Width, target.Height);
                foreach (var layer in layers) Require(layer.Draw(), "An interface layer failed while testing occlusion");
                Require(hits == 0 && player.inventory[playerSlot].stack == 5, "A card click reached the obscured player slot");
                Require(Main.MouseScreen.ToPoint() == overlap.Center, "The inventory guard did not restore UI pointer coordinates");
                if (page == "ShowInventory")
                    Require(bag.Items[0].IsAir && Main.mouseItem.type == savedBag.type && Main.mouseItem.stack == savedBag.stack,
                        "A covered native slot blocked or double-handled the visible companion slot");
                else Require(Main.mouseItem.IsAir, "A profile or mastery click lifted an obscured item");
            }
            nativeSlot = new Rectangle(20, 20, 44, 44); playerSlot = 0;
            player.inventory[0].SetDefaults(Terraria.ID.ItemID.DirtBlock); player.inventory[0].stack = 5;
            Main.mouseItem.TurnToAir(); hits = 0; Main.mouseLeft = Main.mouseLeftRelease = true; Pointer(new Point(22, 22));
            Require(!((UIElement)Field(card, "frame")).ContainsPoint(Main.MouseScreen), "The uncovered input probe still lies beneath the card");
            foreach (var layer in layers) Require(layer.Draw(), "An interface layer failed for uncovered inventory");
            Require(hits == 1 && player.inventory[0].IsAir && Main.mouseItem.type == Terraria.ID.ItemID.DirtBlock,
                "The card blocked the uncovered player inventory");
            Console.WriteLine("PASS interface-layer input: covered slots receive no press on every page, visible bag receives one, uncovered inventory works, pointer and owned inventory mode restored");
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

    private static void VerifyCanvas(Mastery tree)
    {
        var viewport = (UIElement)Field(tree, "viewport");
        Vector2 center = viewport.GetDimensions().Center();
        float fitted = (float)Field(tree, "zoom");
        Main.mouseX = (int)center.X; Main.mouseY = (int)center.Y;
        tree.ScrollWheel(new UIScrollWheelEvent(tree, center, 120));
        Require((float)Field(tree, "zoom") > fitted, "Mastery wheel did not zoom");
        Main.mouseLeft = true; viewport.LeftMouseDown(new UIMouseEvent(viewport, center));
        Main.mouseX += 25; Main.mouseY += 11; tree.Update(new GameTime());
        Main.mouseLeft = false; viewport.LeftMouseUp(new UIMouseEvent(viewport, Main.MouseScreen));
        Require(((Vector2)Field(tree, "pan")).Length() > 20, "Mastery drag did not pan");
        Click(Descendants(tree).OfType<UITextPanel<string>>().Single(b => b.Text == "Reset view"));
        Require((float)Field(tree, "zoom") == fitted && (Vector2)Field(tree, "pan") == Vector2.Zero, "Reset view did not restore the fitted graph");
        Rectangle canvas = viewport.GetDimensions().ToRectangle();
        foreach (var node in GraphData.Nodes)
        {
            var point = (Vector2)tree.GetType().GetMethod("Screen", Private)!.Invoke(tree, new object[] { node.Position })!;
            Require(canvas.Contains(point.ToPoint()), "An authored mastery node starts outside its fitted viewport");
        }
        Main.mouseX = Main.mouseY = -100;
        Console.WriteLine("PASS native mastery viewport: pan, zoom, reset and all authored nodes fit");
    }

    public static void OpenWeaponPage(UIState card)
    {
        var tree = Descendants(card).OfType<Mastery>().Single();
        var viewport = (UIElement)Field(tree, "viewport");
        void Select(Vector2 position)
        {
            Vector2 at = (Vector2)tree.GetType().GetMethod("Screen", Private)!.Invoke(tree, new object[] { position })!;
            Main.mouseX = (int)at.X; Main.mouseY = (int)at.Y;
            Main.mouseLeft = true; viewport.LeftMouseDown(new UIMouseEvent(viewport, at));
            Main.mouseLeft = false; viewport.LeftMouseUp(new UIMouseEvent(viewport, at));
            tree.Update(new GameTime());
        }
        Select(GraphData.Nodes[14].Position);
        Require((int)Field(tree, "selected") == 14, "Clicking the weapon diamond did not select it");
        Click(Descendants(tree).OfType<UITextPanel<string>>().Single(b => b.Text == "Weapon details"));
        Require((int)Field(tree, "weaponBranch") == 1, "Weapon details did not open the ranged subtree");
        Select(new Vector2(-35, -40));
        int before = Main.LocalPlayer.GetModPlayer<live::AICompanion.Companion.PlayerIntegration.CompanionPlayer>().Bag.Items.Sum(item => item.stack);
        Click(Descendants(tree).OfType<UITextPanel<string>>().Single(b => b.Text == "Preview rank"));
        Require(((int[,])Field(tree, "weaponTiers"))[1, 1] == 0, "The nested weapon rank bypassed its incoming path");
        Select(GraphData.WeaponPositions[0]);
        Click(Descendants(tree).OfType<UITextPanel<string>>().Single(b => b.Text == "Preview rank"));
        Select(GraphData.WeaponPositions[1]);
        Click(Descendants(tree).OfType<UITextPanel<string>>().Single(b => b.Text == "Preview rank"));
        Require(((int[,])Field(tree, "weaponTiers"))[1, 1] == 1, "The nested weapon rank did not respond to its real button");
        Require(Main.LocalPlayer.GetModPlayer<live::AICompanion.Companion.PlayerIntegration.CompanionPlayer>().Bag.Items.Sum(item => item.stack) == before, "A weapon preview consumed inventory");
        Main.mouseX = Main.mouseY = -100;
        Console.WriteLine("PASS nested weapon preview: diamond selection, subtree navigation and incoming paths govern ranks without consuming inventory");
    }

    public static void VerifyMastery()
    {
        Require(GraphData.Branches.Length == 8, "The eight mastery branches must survive");
        Require(GraphData.Nodes.Count(n => n.Kind == GraphData.NodeKind.Filler) == 40, "The authored graph must include the path ranks and shared junctions");
        var reachable = new HashSet<int> { -1 };
        for (int pass = 0; pass < GraphData.Nodes.Length; pass++)
            foreach (var edge in GraphData.Edges)
            {
                Require(edge.From >= -1 && edge.From < GraphData.Nodes.Length && edge.To >= 0 && edge.To < GraphData.Nodes.Length, "A mastery edge names a missing node");
                if (reachable.Contains(edge.From)) reachable.Add(edge.To);
            }
        Require(reachable.Count == GraphData.Nodes.Length + 1, "An authored mastery node is disconnected from the start");
        var tree = new Mastery();
        int[] tiers = (int[])Field(tree, "tiers");
        foreach (int node in Enumerable.Range(0, GraphData.Nodes.Length))
        {
            var incoming = GraphData.Edges.Where(edge => edge.To == node && edge.From >= 0).ToArray();
            if (incoming.Length < 2) continue;
            foreach (var edge in incoming)
            {
                Array.Clear(tiers); tiers[edge.From] = 1;
                Require(tree.CanPreview(node), $"Junction {node} requires more than incoming path {edge.From}");
            }
        }
        Array.Clear(tiers);
        tree.GetType().GetField("selected", Private)!.SetValue(tree, 80);
        Call(tree, "PreviewRank"); Require(tiers[80] == 0, "An unopened path previewed a locked node");
        tiers[3] = 1; Call(tree, "PreviewRank"); Require(tiers[80] == 1, "A single incoming path did not enable a shared rank");
        Console.WriteLine($"PASS authored mastery: {GraphData.Nodes.Length} reachable nodes, {GraphData.Edges.Length} directed edges, every convergence accepts each incoming path independently");
    }
}
