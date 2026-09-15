#nullable enable

using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.ModLoader;
using Terraria.UI;
using AICompanion.Companion.CharacterBody;
using AICompanion.Companion.PlayerIntegration;
using AICompanion.Companion.ProfileCard;

namespace AICompanion.Companion.HeadsUpDisplay;

/// <summary>
/// The companion's health, mana and experience as three stacked bars in a notch on the HUD, and nothing
/// else: no name, no level, no readings and no icons, by the owner's ruling of 15 September 2026 that
/// information shown twice, or not needed at a glance, is clutter. The numbers are on the card, which a
/// click on the notch opens.
///
/// Docked at top-centre it hangs from the screen edge like a laptop notch: square top corners flush with the
/// edge, rounded bottom corners, and two concave fillets outside the top corners that make it read as lodged
/// in rather than laid on. Dragged anywhere else it keeps the same body as a free rounded card with a border,
/// and the fillets go. Right-click docks it again. The position is saved per character through
/// <see cref="CompanionPlayer"/>. Nothing animates: a bar snaps.
///
/// Drawn in raw screen pixels, because Terraria's mouse coordinates are raw screen pixels, with sizes scaled
/// by the UI scale by hand.
/// </summary>
public class CompanionHealthBar : ModSystem
{
    /// <summary>The bars' own size and the space around them, in base pixels; the notch is exactly the bars plus that padding.</summary>
    private const int BarWidth = 150;
    private const int BarHeight = 6;
    private const int BarGap = 7;
    private const int Padding = 8;
    public const int BaseWidth = Padding + BarWidth + Padding;
    public const int BaseHeight = Padding + 3 * BarHeight + 2 * BarGap + Padding;
    private const int BaseRadius = 12;
    private const int BaseFillet = 10;
    /// <summary>How far a free notch stays from the screen's side edges.</summary>
    private const int FreeMargin = 8;

    private static readonly Color Body = new(18, 18, 21);
    private static readonly Color Border = new(112, 112, 122);
    private static readonly Color Track = new(44, 44, 50);
    private static readonly Color Healthy = new(52, 199, 89);
    private static readonly Color Hurt = new(255, 69, 58);
    private static readonly Color Downed = new(142, 142, 147);
    /// <summary>The game's own mana-star blue, so the bar says "mana" to anyone who has played Terraria; the card's strip draws the same.</summary>
    private static readonly Color Mana = new(106, 168, 255);
    /// <summary>The card's experience gold, shared with the card's identity strip.</summary>
    private static readonly Color Experience = new(255, 210, 74);

    /// <summary>A press has to travel this far (UI-scaled) before it is a drag; released before that, it is a click.</summary>
    private const float DragThreshold = 6f;

    private static bool pressed;
    private bool dragging;
    private Vector2 pressPoint;
    private Vector2 dragOffset;

    /// <summary>The notch's box on screen: top-centre while docked, the saved position clamped onto the screen while free.</summary>
    public static Rectangle Bounds(CompanionPlayer save)
    {
        float scale = Main.UIScale;
        int width = (int)(BaseWidth * scale), height = (int)(BaseHeight * scale);
        Vector2 pos = save.HealthBarPosition is Vector2 saved ? Clamp(saved, width, height, scale) : new Vector2((Main.screenWidth - width) / 2f, 0f);
        return new Rectangle((int)pos.X, (int)pos.Y, width, height);
    }

    private static Vector2 Clamp(Vector2 pos, int width, int height, float scale)
    {
        float margin = FreeMargin * scale;
        return new Vector2(MathHelper.Clamp(pos.X, margin, Math.Max(margin, Main.screenWidth - width - margin)),
            MathHelper.Clamp(pos.Y, 0f, Math.Max(0f, Main.screenHeight - height)));
    }

    /// <summary>
    /// The three bar tracks inside the notch, health above mana above experience, the same column the card's
    /// identity strip stacks. The padding is the same on all four sides and the two gaps are equal, derived from
    /// the box rather than stored, so the spacing stays even at any UI scale; the drawing and the headless
    /// fixture both read this, so the fixture's geometry is the drawing's by construction.
    /// </summary>
    public static (Rectangle Health, Rectangle Mana, Rectangle Experience) Bars(Rectangle box, float scale)
    {
        int pad = Math.Max(2, (int)MathF.Round(Padding * scale));
        int barH = Math.Max(3, (int)MathF.Round(BarHeight * scale));
        int inner = box.Height - 2 * pad;
        int gap = (inner - 3 * barH) / 2;
        // Rounding leaves at most one spare pixel; it goes above the first bar so top and bottom differ by no more than one.
        int top = box.Y + pad + (inner - 3 * barH - 2 * gap);
        int width = box.Width - 2 * pad;
        return (new Rectangle(box.X + pad, top, width, barH),
            new Rectangle(box.X + pad, top + barH + gap, width, barH),
            new Rectangle(box.X + pad, top + 2 * (barH + gap), width, barH));
    }

    /// <summary>Consume the opening press before item use, including a cursor entering this tick.</summary>
    public static void CaptureInput(CompanionPlayer save)
    {
        if (!Main.gameMenu && Main.LocalPlayer.active && CompanionNPC.Find() != null
            && (pressed || Bounds(save).Contains(Main.mouseX, Main.mouseY)))
            Main.LocalPlayer.mouseInterface = true;
    }

    public override void OnWorldUnload() { pressed = false; dragging = false; }

    public override void ModifyInterfaceLayers(List<GameInterfaceLayer> layers)
    {
        int index = layers.FindIndex(l => l.Name == "Vanilla: Resource Bars");
        if (index < 0)
            index = layers.Count - 1;
        layers.Insert(index + 1, new LegacyGameInterfaceLayer("AICompanion: Companion Health", Draw, InterfaceScaleType.None));
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
        Rectangle box = Bounds(save);

        Vector2 mouse = new(Main.mouseX, Main.mouseY);
        bool hovering = box.Contains(mouse.ToPoint());
        if (hovering || pressed)
            Main.LocalPlayer.mouseInterface = true;

        // A press is a click until it travels: the notch only moves once the button has been
        // held through DragThreshold pixels, and a release before that opens the profile.
        if (pressed)
        {
            if (Main.mouseLeft)
            {
                if (!dragging && Vector2.Distance(mouse, pressPoint) > DragThreshold * scale)
                    dragging = true;
                if (dragging)
                {
                    Vector2 pos = Clamp(mouse - dragOffset, box.Width, box.Height, scale);
                    save.HealthBarPosition = pos;
                    box.Location = pos.ToPoint();
                }
            }
            else
            {
                if (!dragging)
                    CompanionProfileCardSystem.Toggle();
                pressed = false;
                dragging = false;
            }
        }
        else if (hovering && Main.mouseLeft && Main.mouseLeftRelease)
        {
            pressed = true;
            pressPoint = mouse;
            dragOffset = mouse - box.Location.ToVector2();
        }

        if (hovering && Main.mouseRight && Main.mouseRightRelease)
        {
            save.HealthBarPosition = null;
            pressed = false;
            dragging = false;
            box = Bounds(save);
        }

        DrawNotch(Main.spriteBatch, box, save.HealthBarPosition == null, npc, companion, save, scale);
        return true;
    }

    private static void DrawNotch(SpriteBatch sb, Rectangle box, bool docked, NPC npc, CompanionNPC companion, CompanionPlayer save, float scale)
    {
        int radius = Math.Max(4, (int)(BaseRadius * scale));
        int fillet = Math.Max(4, (int)(BaseFillet * scale));
        int border = Math.Max(1, (int)MathF.Round(scale));

        if (docked)
        {
            // Body with only the bottom corners rounded, flush with the screen edge. The border layer is the
            // same shape one border wider on the sides and bottom, so a hairline follows the silhouette and the
            // notch reads against a night sky; the top stays flush with no line, because the edge it hangs from
            // is the screen.
            DrawCardPrimitives.RoundedFill(sb, box, radius, DrawCardPrimitives.BottomCorners, Border);
            Rectangle inner = new(box.X + border, box.Y, box.Width - 2 * border, box.Height - border);
            DrawCardPrimitives.RoundedFill(sb, inner, Math.Max(2, radius - border), DrawCardPrimitives.BottomCorners, Body);
            // Concave fillets outside the top corners: a square with a quarter circle cut out, so the notch reads
            // as part of the edge. The border fillet has the plain radius and the body fillet a radius one border
            // larger about the same centre, leaving a hairline along the curve that meets the side lines.
            sb.Draw(DrawCardPrimitives.FilletMask(sb, fillet, fillet, flipX: false), new Rectangle(box.X - fillet, box.Y, fillet, fillet), Border);
            sb.Draw(DrawCardPrimitives.FilletMask(sb, fillet, fillet, flipX: true), new Rectangle(box.Right, box.Y, fillet, fillet), Border);
            sb.Draw(DrawCardPrimitives.FilletMask(sb, fillet, fillet + border, flipX: false), new Rectangle(box.X - fillet, box.Y, fillet, fillet), Body);
            sb.Draw(DrawCardPrimitives.FilletMask(sb, fillet, fillet + border, flipX: true), new Rectangle(box.Right, box.Y, fillet, fillet), Body);
        }
        else
        {
            DrawCardPrimitives.RoundedFill(sb, box, radius, DrawCardPrimitives.AllCorners, Border);
            Rectangle inner = new(box.X + border, box.Y + border, box.Width - 2 * border, box.Height - 2 * border);
            DrawCardPrimitives.RoundedFill(sb, inner, Math.Max(2, radius - border), DrawCardPrimitives.AllCorners, Body);
        }

        var bars = Bars(box, scale);
        // Downed, the health bar is grey and fills with revival rather than life, so the one bar says both that the
        // companion is down and how close it is to getting up; the colour is the only state the notch still carries.
        float health = companion.IsDowned
            ? MathHelper.Clamp(companion.RevivePercent / 100f, 0f, 1f)
            : npc.lifeMax > 0 ? MathHelper.Clamp(npc.life / (float)npc.lifeMax, 0f, 1f) : 0f;
        DrawBar(sb, bars.Health, health, companion.IsDowned ? Downed : Color.Lerp(Hurt, Healthy, health));
        DrawBar(sb, bars.Mana, companion.Mana.Fraction, Mana);
        DrawBar(sb, bars.Experience, save.Experience.Fraction, Experience);
    }

    /// <summary>A rounded track with a rounded fill of the given fraction; a fill narrower than the bar's own height is not drawn, because the mask cannot round it.</summary>
    private static void DrawBar(SpriteBatch sb, Rectangle track, float fraction, Color fill)
    {
        DrawCardPrimitives.RoundedFill(sb, track, track.Height / 2, DrawCardPrimitives.AllCorners, Track);
        int fillW = (int)(track.Width * MathHelper.Clamp(fraction, 0f, 1f));
        if (fillW >= track.Height)
            DrawCardPrimitives.RoundedFill(sb, new Rectangle(track.X, track.Y, fillW, track.Height), track.Height / 2, DrawCardPrimitives.AllCorners, fill);
    }
}
