#nullable enable

using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.ModLoader;
using Terraria.UI;
using AICompanion.Companion.CharacterBody;
using AICompanion.Companion.PlayerIntegration;
using AICompanion.Companion.ProfileCard;

namespace AICompanion.Companion.HeadsUpDisplay;

/// <summary>
/// The companion's health, mana and experience as a notch on the HUD. Docked at top-centre
/// it hangs from the screen edge like a laptop notch: square top corners flush with the
/// edge, rounded bottom corners, and two concave fillets outside the top corners that make
/// it read as lodged in rather than laid on. Dragged anywhere else it keeps the same body as
/// a free rounded card with a border, and the fillets go. Right-click docks it again. The
/// position is saved per character through <see cref="CompanionPlayer"/>.
///
/// Health is the hero, a full-width pill under the name and reading; under it mana and
/// experience share a row as two half-width pills, each with its own reading above it, so
/// the notch reads as one thing the companion can lose above two things that tire and grow.
/// Mana's hover reading says what the pool does to a cast, because a bar that only ever
/// drains would read as something the companion runs out of, and it never does (see
/// <see cref="Weapons.CompanionMana"/>). Experience reads the level beside the fraction
/// toward the next, the owner's "level and experience with a slash". Nothing animates:
/// a bar snaps, as the notch always has.
///
/// Shapes are runtime-built alpha masks (a rounded rectangle and a fillet), tinted
/// when drawn and cached per pixel size, because the HUD has no rounded primitives.
/// Drawn in screen pixels, sizes scaled by the UI scale by hand.
/// </summary>
public class CompanionHealthBar : ModSystem
{
    private const int BaseWidth = 232;
    private const int BaseHeight = 60;
    private const int BaseRadius = 12;
    private const int BaseFillet = 10;
    private const int IconSide = 22;
    private const int IconGap = 8;
    private const int IconPadding = 8;

    /// <summary>
    /// The rows inside the notch, in base pixels from its top: the name reading, the health
    /// pill, the two small readings, the two small pills. The bottom pad under the last pill is
    /// what closes the height to <see cref="BaseHeight"/>. The card's identity strip stacks the
    /// same three bars in the same order, so a player learns the column once.
    /// </summary>
    private const int ReadingRow = 3;
    private const int HealthRow = 20;
    private const int HealthPillHeight = 8;
    private const int SmallReadingRow = 31;
    private const int SmallRow = 45;
    private const int SmallPillHeight = 6;
    private const int SidePad = 8;

    private static readonly Color Body = new(18, 18, 21);
    private static readonly Color Border = new(112, 112, 122);
    private static readonly Color Track = new(44, 44, 50);
    private static readonly Color Healthy = new(52, 199, 89);
    private static readonly Color Hurt = new(255, 69, 58);
    private static readonly Color Downed = new(142, 142, 147);
    /// <summary>The game's own mana-star blue, so the bar says "mana" to anyone who has played Terraria.</summary>
    private static readonly Color Mana = new(106, 168, 255);
    /// <summary>The card mock's experience gold, shared with the card's identity strip.</summary>
    private static readonly Color Experience = new(255, 210, 74);
    private static readonly Color Ink = new(235, 235, 240);

    /// <summary>A press has to travel this far (UI-scaled) before it is a drag; released before that, it is a click.</summary>
    private const float DragThreshold = 6f;

    private static bool pressed;
    private bool dragging;
    private Vector2 pressPoint;
    private Vector2 dragOffset;

    private static readonly Dictionary<(int w, int h, int r, int corners), Texture2D> masks = new();

    public static Rectangle Bounds(CompanionPlayer save)
    {
        Rectangle box = HealthBounds(save);
        box.Inflate((int)((IconSide + IconGap + IconPadding) * Main.UIScale), 0);
        return box;
    }

    public static Rectangle HealthBounds(CompanionPlayer save)
    {
        int width = (int)(BaseWidth * Main.UIScale), height = (int)(BaseHeight * Main.UIScale);
        Vector2 pos = save.HealthBarPosition ?? new Vector2((Main.screenWidth - width) / 2f, 0f);
        float margin = (IconSide + IconGap + IconPadding) * Main.UIScale;
        pos.X = MathHelper.Clamp(pos.X, margin, Math.Max(margin, Main.screenWidth - width - margin));
        pos.Y = MathHelper.Clamp(pos.Y, 0, Math.Max(0, Main.screenHeight - height));
        return new Rectangle((int)pos.X, (int)pos.Y, width, height);
    }

    public static Rectangle IconBounds(Rectangle health, bool family, float scale)
    {
        int side = (int)(IconSide * scale), gap = (int)(IconGap * scale);
        return new Rectangle(family ? health.X - gap - side : health.Right + gap,
            health.Center.Y - side / 2, side, side);
    }

    /// <summary>
    /// The three pill tracks inside the notch body: health full width, then mana on the left
    /// half and experience on the right, a side pad apart. The drawing and the headless
    /// fixture both read this, so the fixture's geometry is the drawing's by construction.
    /// </summary>
    public static (Rectangle Health, Rectangle Mana, Rectangle Experience) Bars(Rectangle box, float scale)
    {
        int pad = (int)(SidePad * scale);
        int healthH = Math.Max(4, (int)(HealthPillHeight * scale));
        int smallH = Math.Max(3, (int)(SmallPillHeight * scale));
        int halfW = (box.Width - 3 * pad) / 2;
        return (new Rectangle(box.X + pad, box.Y + (int)(HealthRow * scale), box.Width - 2 * pad, healthH),
            new Rectangle(box.X + pad, box.Y + (int)(SmallRow * scale), halfW, smallH),
            new Rectangle(box.Right - pad - halfW, box.Y + (int)(SmallRow * scale), halfW, smallH));
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
        Rectangle box = HealthBounds(save);
        Vector2 pos = box.Location.ToVector2();

        Vector2 mouse = new(Main.mouseX, Main.mouseY);
        bool hovering = Bounds(save).Contains(mouse.ToPoint());
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
                    pos = mouse - dragOffset;
                    float margin = (IconSide + IconGap + IconPadding) * scale;
                    pos.X = MathHelper.Clamp(pos.X, margin, Math.Max(margin, Main.screenWidth - width - margin));
                    pos.Y = MathHelper.Clamp(pos.Y, 0f, Main.screenHeight - height);
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
        DrawNotch(box, docked, npc, companion, save, scale);
        DrawActivityIcons(box, companion, scale);
        return true;
    }

    private static void DrawActivityIcons(Rectangle box, CompanionNPC companion, float scale)
    {
        var state = companion.Brain.Presentation;
        DrawIcon(IconBounds(box, true, scale), DescribeCompanionHud.Family(state), scale);
        DrawIcon(IconBounds(box, false, scale), DescribeCompanionHud.Activity(state), scale);
        if (state.MovementStalled && !state.Suspended)
        {
            Utils.DrawBorderStringFourWay(Main.spriteBatch, FontAssets.MouseText.Value, "Stuck", box.Center.X, box.Bottom + 3 * scale,
                Color.Gold, Color.Black, new Vector2(FontAssets.MouseText.Value.MeasureString("Stuck").X / 2f, 0f), .65f * scale);
        }
    }

    private static void DrawIcon(Rectangle slot, HudIcon icon, float scale)
    {
        DrawPurposeSymbol.Draw(Main.spriteBatch, slot, icon.Symbol,
            icon.Symbol == HudSymbol.None ? Downed : icon.Subdued ? Color.White * .4f : Color.White);
        if (slot.Contains(Main.mouseX, Main.mouseY)) Main.instance.MouseText(icon.Name);
    }

    private static void DrawNotch(Rectangle box, bool docked, NPC npc, CompanionNPC companion, CompanionPlayer save, float scale)
    {
        SpriteBatch sb = Main.spriteBatch;
        int radius = Math.Max(4, (int)(BaseRadius * scale));
        int fillet = Math.Max(4, (int)(BaseFillet * scale));
        int border = Math.Max(1, (int)MathF.Round(scale));
        Rectangle health = box;
        box.Inflate((int)((IconSide + IconGap + IconPadding) * scale), 0);

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

        box = health;
        // The pills occupy the centre; the two icon wings share the enclosing body.
        var bars = Bars(box, scale);
        float fraction = companion.IsDowned
            ? MathHelper.Clamp(companion.RevivePercent / 100f, 0f, 1f)
            : npc.lifeMax > 0 ? MathHelper.Clamp(npc.life / (float)npc.lifeMax, 0f, 1f) : 0f;
        DrawPill(sb, bars.Health, fraction, companion.IsDowned ? Downed : Color.Lerp(Hurt, Healthy, fraction));

        string label = companion.IsDowned
            ? $"Companion   downed {companion.RevivePercent}%"
            : $"Companion   {npc.life} / {npc.lifeMax}";
        float textScale = 0.72f * scale;
        Vector2 size = FontAssets.MouseText.Value.MeasureString(label) * textScale;
        DrawReading(sb, label, new Vector2(box.Center.X - size.X / 2f, box.Y + (int)(ReadingRow * scale)), Ink, textScale);

        // Mana and experience: a reading over each half-width pill, the mana reading flush
        // left and the experience reading flush right, so the two readings frame the row the
        // way the name and the health reading frame the row above.
        Weapons.CompanionMana mana = companion.Mana;
        Progression.CompanionExperience experience = save.Experience;
        DrawPill(sb, bars.Mana, mana.Fraction, Mana);
        DrawPill(sb, bars.Experience, experience.Fraction, Experience);
        float smallScale = 0.6f * scale;
        int readingY = box.Y + (int)(SmallReadingRow * scale);
        string manaReading = $"Mana {(int)MathF.Round(mana.Current)} / {mana.Max}";
        string experienceReading = $"Lv {experience.Level}   {experience.IntoLevel} / {experience.NeededNow}";
        DrawReading(sb, manaReading, new Vector2(bars.Mana.X, readingY), Mana, smallScale);
        float experienceW = FontAssets.MouseText.Value.MeasureString(experienceReading).X * smallScale;
        DrawReading(sb, experienceReading, new Vector2(bars.Experience.Right - experienceW, readingY), Experience, smallScale);

        // Hovering either small bar, or its reading, says what the number does: the pool's
        // effect on a cast, and how far the level is from the next.
        Rectangle manaHover = new(bars.Mana.X, readingY, bars.Mana.Width, bars.Mana.Bottom - readingY);
        Rectangle experienceHover = new(bars.Experience.X, readingY, bars.Experience.Width, bars.Experience.Bottom - readingY);
        if (manaHover.Contains(Main.mouseX, Main.mouseY))
            Main.instance.MouseText($"Mana {(int)MathF.Round(mana.Current)} / {mana.Max} · spells land at {mana.DamageFactor:P0}");
        else if (experienceHover.Contains(Main.mouseX, Main.mouseY))
            Main.instance.MouseText($"Level {experience.Level} · {experience.IntoLevel} / {experience.NeededNow} to level {experience.Level + 1}");
    }

    /// <summary>A rounded track with a rounded fill of the given fraction; a fill narrower than the pill's own height is not drawn, because the mask cannot round it.</summary>
    private static void DrawPill(SpriteBatch sb, Rectangle track, float fraction, Color fill)
    {
        sb.Draw(RoundedMask(track.Width, track.Height, track.Height / 2, corners: 0b1111), track, Track);
        int fillW = (int)(track.Width * MathHelper.Clamp(fraction, 0f, 1f));
        if (fillW >= track.Height)
            sb.Draw(RoundedMask(fillW, track.Height, track.Height / 2, corners: 0b1111), new Rectangle(track.X, track.Y, fillW, track.Height), fill);
    }

    private static void DrawReading(SpriteBatch sb, string text, Vector2 at, Color colour, float textScale)
        => Utils.DrawBorderStringFourWay(sb, FontAssets.MouseText.Value, text, at.X, at.Y, colour, Body, Vector2.Zero, textScale);

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
        var tex = new Texture2D(Main.spriteBatch.GraphicsDevice, w, h);
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
        var tex = new Texture2D(Main.spriteBatch.GraphicsDevice, s, s);
        tex.SetData(data);
        masks[key] = tex;
        return tex;
    }
}
