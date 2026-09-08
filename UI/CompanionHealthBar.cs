#nullable enable

using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.UI;
using AICompanion.Brain.Work.Chopping;
using AICompanion.Brain.Work.Mining;
using AICompanion.Companion;
using AICompanion.Players;

namespace AICompanion.UI;

/// <summary>
/// The companion's health as a notch on the HUD. Docked at top-centre it hangs from the
/// screen edge like a laptop notch: square top corners flush with the edge, rounded
/// bottom corners, and two concave fillets outside the top corners that make it read as
/// lodged in rather than laid on. Dragged anywhere else it keeps the same body as a
/// free rounded card with a border, and the fillets go. Right-click docks it again. The
/// position is saved per character through <see cref="CompanionPlayer"/>.
///
/// Shapes are runtime-built alpha masks (a rounded rectangle and a fillet), tinted
/// when drawn and cached per pixel size, because the HUD has no rounded primitives.
/// Drawn in screen pixels, sizes scaled by the UI scale by hand.
/// </summary>
public class CompanionHealthBar : ModSystem
{
    private const int BaseWidth = 232;
    private const int BaseHeight = 30;
    private const int BaseRadius = 12;
    private const int BaseFillet = 10;

    private static readonly Color Body = new(18, 18, 21);
    private static readonly Color Border = new(112, 112, 122);
    private static readonly Color Track = new(44, 44, 50);
    private static readonly Color Healthy = new(52, 199, 89);
    private static readonly Color Hurt = new(255, 69, 58);
    private static readonly Color Downed = new(142, 142, 147);

    /// <summary>A press has to travel this far (UI-scaled) before it is a drag; released before that, it is a click.</summary>
    private const float DragThreshold = 6f;

    private bool pressed;
    private bool dragging;
    private Vector2 pressPoint;
    private Vector2 dragOffset;

    private static readonly Dictionary<(int w, int h, int r, int corners), Texture2D> masks = new();

    public override void ModifyInterfaceLayers(List<GameInterfaceLayer> layers)
    {
        int index = layers.FindIndex(l => l.Name == "Vanilla: Resource Bars");
        if (index < 0)
            index = layers.Count - 1;
        layers.Insert(index + 1, new LegacyGameInterfaceLayer("AICompanion: Companion Health", Draw, InterfaceScaleType.None));
    }

    public override void Unload()
    {
        // Unload runs on a worker thread and FNA3D refuses to release a texture anywhere but the
        // main thread (ThreadStateException, and tModLoader then reports the mod as unable to
        // unload). The masks are handed to the game's main-thread queue and released there.
        var toDispose = new List<Texture2D>(masks.Values);
        masks.Clear();
        Main.QueueMainThreadAction(() =>
        {
            foreach (Texture2D t in toDispose)
                t.Dispose();
        });
    }

    private bool Draw()
    {
        if (Main.gameMenu || !Main.LocalPlayer.active)
            return true;
        NPC? npc = CompanionNPC.Find();
        if (npc?.ModNPC is not CompanionNPC companion)
            return true;

        CompanionPlayer save = Main.LocalPlayer.GetModPlayer<CompanionPlayer>();
        float scale = Main.UIScale;
        int width = (int)(BaseWidth * scale), height = (int)(BaseHeight * scale);
        Vector2 defaultPos = new((Main.screenWidth - width) / 2f, 0f);
        Vector2 pos = save.HealthBarPosition ?? defaultPos;
        Rectangle box = new((int)pos.X, (int)pos.Y, width, height);

        Vector2 mouse = new(Main.mouseX, Main.mouseY);
        bool hovering = box.Contains(mouse.ToPoint());
        if (hovering || pressed)
            Main.LocalPlayer.mouseInterface = true;

        // A press is a click until it travels: the notch only moves once the button has been
        // held through DragThreshold pixels, and a release before that opens the bag, so the
        // notch is the bag's button as well as its handle.
        if (pressed)
        {
            if (Main.mouseLeft)
            {
                if (!dragging && Vector2.Distance(mouse, pressPoint) > DragThreshold * scale)
                    dragging = true;
                if (dragging)
                {
                    pos = mouse - dragOffset;
                    pos.X = MathHelper.Clamp(pos.X, 0f, Main.screenWidth - width);
                    pos.Y = MathHelper.Clamp(pos.Y, 0f, Main.screenHeight - height);
                    save.HealthBarPosition = pos;
                    box.Location = pos.ToPoint();
                }
            }
            else
            {
                if (!dragging)
                    Inventory.CompanionBagSystem.Toggle();
                pressed = false;
                dragging = false;
            }
        }
        else if (hovering && Main.mouseLeft && Main.mouseLeftRelease)
        {
            pressed = true;
            pressPoint = mouse;
            dragOffset = mouse - pos;
        }

        if (hovering && Main.mouseRight && Main.mouseRightRelease)
        {
            save.HealthBarPosition = null;
            pressed = false;
            dragging = false;
            box.Location = defaultPos.ToPoint();
        }

        bool docked = save.HealthBarPosition == null;
        DrawNotch(box, docked, npc, companion, scale);
        DrawModeIcon(box, npc, companion, scale, hovering);
        return true;
    }

    /// <summary>
    /// What the companion is doing, as one of the game's own item sprites to the left of the
    /// notch, so the mode is readable without the debug overlay: the tool for a job, the
    /// weapon for a fight, a shield for guarding, boots for kiting, a coin for looting, a
    /// compass for following, a sunflower for wandering, a torch when it is holding one up,
    /// a tombstone when it is down. Hover names the action.
    /// </summary>
    private static void DrawModeIcon(Rectangle box, NPC npc, CompanionNPC companion, float scale, bool hoveringNotch)
    {
        (int itemType, string name) = ModeOf(npc, companion);
        if (itemType <= 0)
            return;
        Main.instance.LoadItem(itemType);
        Texture2D tex = TextureAssets.Item[itemType].Value;
        Rectangle frame = Main.itemAnimations[itemType]?.GetFrame(tex) ?? tex.Bounds;
        int side = (int)(22 * scale);
        float fit = Math.Min(side / (float)frame.Width, side / (float)frame.Height);
        int gap = (int)(8 * scale);
        Rectangle slot = new(box.X - gap - side, box.Y + (box.Height - side) / 2, side, side);
        Vector2 centre = slot.Center.ToVector2();
        Main.spriteBatch.Draw(tex, centre, frame, Color.White, 0f, frame.Size() / 2f, fit, SpriteEffects.None, 0f);

        if (slot.Contains(Main.mouseX, Main.mouseY) && !hoveringNotch)
            Main.instance.MouseText(name);
    }

    private static (int, string) ModeOf(NPC npc, CompanionNPC companion)
    {
        if (companion.IsDowned)
            return (ItemID.Tombstone, "downed");
        if (companion.Brain.Reflexes.Active is string reflex)
            return (ItemID.Feather, reflex);
        if (companion.Torch.Shown)
            return (ItemID.Torch, "torch");
        string action = companion.Brain.LastAction?.Name ?? "";
        Player player = Main.LocalPlayer;
        return action switch
        {
            "survive" => (ItemID.BreathingReed, "saving itself"),
            "guard" => (ItemID.CobaltShield, "guarding you"),
            "kite" => (ItemID.HermesBoots, "kiting"),
            "hunt" => (companion.Arsenal.LastChosen?.ItemType ?? companion.Arsenal.Primary.ItemType, "hunting"),
            "loot" => (ItemID.GoldCoin, "looting"),
            "chop" => (TileChopper.AxeFor(player).type, "chopping"),
            "mine" => (TileMiner.PickaxeFor(player).type, "mining"),
            "walk-with" => (ItemID.Compass, "following you"),
            "wander" => (ItemID.Sunflower, "wandering"),
            _ => (0, action),
        };
    }

    private static void DrawNotch(Rectangle box, bool docked, NPC npc, CompanionNPC companion, float scale)
    {
        SpriteBatch sb = Main.spriteBatch;
        int radius = Math.Max(4, (int)(BaseRadius * scale));
        int fillet = Math.Max(4, (int)(BaseFillet * scale));
        int border = Math.Max(1, (int)MathF.Round(scale));

        if (docked)
        {
            // Body with only the bottom corners rounded, flush with the screen edge. The border
            // layer is the same shape one border wider on the sides and bottom, so a hairline
            // follows the silhouette and the notch reads against a night sky; the top stays
            // flush with no line, because the edge it hangs from is the screen.
            sb.Draw(RoundedMask(box.Width, box.Height, radius, corners: 0b1100), box, Border);
            Rectangle inner = new(box.X + border, box.Y, box.Width - 2 * border, box.Height - border);
            sb.Draw(RoundedMask(inner.Width, inner.Height, Math.Max(2, radius - border), corners: 0b1100), inner, Body);
            // Concave fillets outside the top corners: a square with a quarter circle cut out, so
            // the notch reads as part of the edge. The border fillet has the plain radius and the
            // body fillet a radius one border larger about the same centre, leaving a hairline
            // along the curve that meets the side lines.
            sb.Draw(FilletMask(fillet, fillet, flipX: false), new Rectangle(box.X - fillet, box.Y, fillet, fillet), Border);
            sb.Draw(FilletMask(fillet, fillet, flipX: true), new Rectangle(box.Right, box.Y, fillet, fillet), Border);
            sb.Draw(FilletMask(fillet, fillet + border, flipX: false), new Rectangle(box.X - fillet, box.Y, fillet, fillet), Body);
            sb.Draw(FilletMask(fillet, fillet + border, flipX: true), new Rectangle(box.Right, box.Y, fillet, fillet), Body);
        }
        else
        {
            sb.Draw(RoundedMask(box.Width, box.Height, radius, corners: 0b1111), box, Border);
            Rectangle inner = new(box.X + border, box.Y + border, box.Width - 2 * border, box.Height - 2 * border);
            sb.Draw(RoundedMask(inner.Width, inner.Height, Math.Max(2, radius - border), corners: 0b1111), inner, Body);
        }

        // The pill: a track and a fill, both fully rounded, inset from the body.
        int pad = (int)(8 * scale);
        int pillH = Math.Max(4, (int)(8 * scale));
        Rectangle track = new(box.X + pad, box.Bottom - pad - pillH, box.Width - 2 * pad, pillH);
        sb.Draw(RoundedMask(track.Width, track.Height, track.Height / 2, corners: 0b1111), track, Track);
        float fraction = npc.lifeMax > 0 ? MathHelper.Clamp(npc.life / (float)npc.lifeMax, 0f, 1f) : 0f;
        int fillW = (int)(track.Width * fraction);
        if (fillW >= pillH)
        {
            Color fill = companion.IsDowned ? Downed : Color.Lerp(Hurt, Healthy, fraction);
            sb.Draw(RoundedMask(fillW, track.Height, track.Height / 2, corners: 0b1111), new Rectangle(track.X, track.Y, fillW, track.Height), fill);
        }

        string label = companion.IsDowned
            ? $"Companion   downed {companion.RevivePercent}%"
            : $"Companion   {npc.life} / {npc.lifeMax}";
        float textScale = 0.72f * scale;
        Vector2 size = FontAssets.MouseText.Value.MeasureString(label) * textScale;
        Vector2 textPos = new(box.Center.X - size.X / 2f, box.Y + (int)(3 * scale));
        Utils.DrawBorderStringFourWay(sb, FontAssets.MouseText.Value, label, textPos.X, textPos.Y, new Color(235, 235, 240), Body, Vector2.Zero, textScale);
    }

    /// <summary>A white alpha mask of a rectangle with the chosen corners rounded (bits: 1 top-left, 2 top-right, 4 bottom-left, 8 bottom-right).</summary>
    private static Texture2D RoundedMask(int w, int h, int r, int corners)
    {
        var key = (w, h, r, corners);
        if (masks.TryGetValue(key, out Texture2D? cached))
            return cached;
        var data = new Color[w * h];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                float a = 1f;
                (int cx, int cy, bool rounded)? corner = null;
                if (x < r && y < r && (corners & 1) != 0) corner = (r, r, true);
                else if (x >= w - r && y < r && (corners & 2) != 0) corner = (w - r - 1, r, true);
                else if (x < r && y >= h - r && (corners & 4) != 0) corner = (r, h - r - 1, true);
                else if (x >= w - r && y >= h - r && (corners & 8) != 0) corner = (w - r - 1, h - r - 1, true);
                if (corner is (int cx, int cy, _))
                {
                    float d = MathF.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                    a = MathHelper.Clamp(r - d + 0.5f, 0f, 1f); // one-pixel anti-aliased edge
                }
                data[y * w + x] = Color.White * a;
            }
        }
        var tex = new Texture2D(Main.instance.GraphicsDevice, w, h);
        tex.SetData(data);
        masks[key] = tex;
        return tex;
    }

    /// <summary>
    /// A square of side <paramref name="s"/> with a circle of <paramref name="radius"/> about its
    /// outer bottom corner removed, so it fills the concave gap beside the notch's top corner. A
    /// radius larger than the side leaves a thinner sliver about the same centre, which is how
    /// the border and body fillets nest.
    /// </summary>
    private static Texture2D FilletMask(int s, int radius, bool flipX)
    {
        var key = (s, radius, -1, flipX ? 1 : 0);
        if (masks.TryGetValue(key, out Texture2D? cached))
            return cached;
        var data = new Color[s * s];
        for (int y = 0; y < s; y++)
        {
            for (int x = 0; x < s; x++)
            {
                // The circle's centre is the outer bottom corner: bottom-left for the left fillet, bottom-right for the right.
                float cx = flipX ? s : 0f, cy = s;
                float d = MathF.Sqrt((x + 0.5f - cx) * (x + 0.5f - cx) + (y + 0.5f - cy) * (y + 0.5f - cy));
                float a = MathHelper.Clamp(d - radius + 0.5f, 0f, 1f);
                data[y * s + x] = Color.White * a;
            }
        }
        var tex = new Texture2D(Main.instance.GraphicsDevice, s, s);
        tex.SetData(data);
        masks[key] = tex;
        return tex;
    }
}
