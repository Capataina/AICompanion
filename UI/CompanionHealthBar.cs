#nullable enable

using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.ModLoader;
using Terraria.UI;
using AICompanion.Content;
using AICompanion.Players;

namespace AICompanion.UI;

/// <summary>
/// The companion's health bar on the HUD. Starts top-centre, drags with the left
/// mouse button, and snaps back to top-centre on right-click. Its position is saved
/// per character through <see cref="CompanionPlayer"/>. Drawn in screen pixels, with
/// sizes scaled by the UI scale so it matches the rest of the HUD.
/// </summary>
public class CompanionHealthBar : ModSystem
{
    private const int BaseWidth = 220;
    private const int BaseHeight = 22;
    private const int TopMargin = 8;

    private bool dragging;
    private Vector2 dragOffset;

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
        NPC? npc = Companion.Find();
        if (npc?.ModNPC is not Companion companion)
            return true;

        CompanionPlayer save = Main.LocalPlayer.GetModPlayer<CompanionPlayer>();
        float scale = Main.UIScale;
        int width = (int)(BaseWidth * scale), height = (int)(BaseHeight * scale);
        Vector2 defaultPos = new((Main.screenWidth - width) / 2f, TopMargin * scale);
        Vector2 pos = save.HealthBarPosition ?? defaultPos;
        Rectangle box = new((int)pos.X, (int)pos.Y, width, height);

        Vector2 mouse = new(Main.mouseX, Main.mouseY);
        bool hovering = box.Contains(mouse.ToPoint());
        if (hovering || dragging)
            Main.LocalPlayer.mouseInterface = true;

        if (dragging)
        {
            if (Main.mouseLeft)
            {
                pos = mouse - dragOffset;
                pos.X = MathHelper.Clamp(pos.X, 0f, Main.screenWidth - width);
                pos.Y = MathHelper.Clamp(pos.Y, 0f, Main.screenHeight - height);
                save.HealthBarPosition = pos;
                box.Location = pos.ToPoint();
            }
            else
            {
                dragging = false;
            }
        }
        else if (hovering && Main.mouseLeft && Main.mouseLeftRelease)
        {
            dragging = true;
            dragOffset = mouse - pos;
        }

        if (hovering && Main.mouseRight && Main.mouseRightRelease)
        {
            save.HealthBarPosition = null;
            dragging = false;
            box.Location = defaultPos.ToPoint();
        }

        DrawBar(box, npc, companion, scale);
        return true;
    }

    private static void DrawBar(Rectangle box, NPC npc, Companion companion, float scale)
    {
        Texture2D pixel = TextureAssets.MagicPixel.Value;
        SpriteBatch sb = Main.spriteBatch;

        sb.Draw(pixel, box, new Color(0, 0, 0, 180));
        Rectangle inner = new(box.X + 2, box.Y + 2, box.Width - 4, box.Height - 4);
        float fraction = npc.lifeMax > 0 ? MathHelper.Clamp(npc.life / (float)npc.lifeMax, 0f, 1f) : 0f;
        Color fill = companion.IsDowned ? new Color(120, 120, 120) : Color.Lerp(new Color(200, 40, 40), new Color(60, 200, 80), fraction);
        sb.Draw(pixel, new Rectangle(inner.X, inner.Y, (int)(inner.Width * fraction), inner.Height), fill);

        string label = companion.IsDowned
            ? $"Companion  downed  {companion.RevivePercent}%"
            : $"Companion  {npc.life}/{npc.lifeMax}";
        Vector2 size = FontAssets.MouseText.Value.MeasureString(label) * 0.8f * scale;
        Vector2 textPos = new(box.Center.X - size.X / 2f, box.Center.Y - size.Y / 2f);
        Utils.DrawBorderString(sb, label, textPos, Color.White, 0.8f * scale);
    }
}
