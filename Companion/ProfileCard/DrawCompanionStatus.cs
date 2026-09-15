#nullable enable
using System;
using System.Globalization;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.UI;
using AICompanion.Companion.CharacterBody;
using AICompanion.Companion.PlayerIntegration;

namespace AICompanion.Companion.ProfileCard;

/// <summary>
/// The overview's identity strip: the drone's portrait, the name and level, and health, mana and experience as
/// three large bars filling the strip under the name, each reading beside its bar in the bar's colour, with the
/// three work controls on the right. The action line that used to sit under the bars is gone by the owner's
/// ruling of 15 September 2026, because information shown twice, or not needed at a glance, is clutter, and
/// the sentence that composed it went with it, since nothing else read it. Displaying never chooses an action.
/// </summary>
public sealed class DrawCompanionStatus : UIElement
{
    public const float NameLeft = 80, BarsTop = 30, BarWidth = 220, BarHeight = 16, BarStep = 25, ReadingGap = 8;
    public const float ReadingScale = .75f, ControlsLeft = 430;

    public static readonly Color HealthFill = new(140, 240, 140);
    /// <summary>The game's own mana-star blue and the card's experience gold; the HUD notch draws the same two.</summary>
    public static readonly Color ManaFill = new(106, 168, 255);
    public static readonly Color ExperienceFill = new(255, 210, 74);
    private static readonly Color Gold = new(255, 224, 102);
    private static readonly Color Sunk = new Color(37, 41, 122) * .75f;
    private static readonly Color BarTrack = new(22, 22, 69);
    private static readonly Color DownedFill = new(142, 142, 147);

    public ControlWorkPreferences Controls { get; }

    public DrawCompanionStatus()
    {
        Controls = new ControlWorkPreferences();
        Controls.Left.Set(ControlsLeft, 0); Controls.Width.Set(-ControlsLeft, 1f); Controls.Height.Set(0, 1f);
        Append(Controls);
    }

    /// <summary>The portrait's box inside a strip.</summary>
    public static Rectangle Portrait(Rectangle strip) => new(strip.X + 2, strip.Y + 3, 66, 92);

    /// <summary>The three bars' boxes inside a strip: health, mana, experience.</summary>
    public static Rectangle[] Bars(Rectangle strip) => new[] { Bar(strip, 0), Bar(strip, 1), Bar(strip, 2) };

    /// <summary>One bar's box inside a strip, 0 health, 1 mana, 2 experience; the drawing asks for each, so a frame builds no array.</summary>
    public static Rectangle Bar(Rectangle strip, int index)
        => new(strip.X + (int)NameLeft, strip.Y + (int)(BarsTop + index * BarStep), (int)BarWidth, (int)BarHeight);

    private static string Number(long value) => value.ToString("N0", CultureInfo.InvariantCulture);

    /// <summary>The strip's readings, each rebuilt only when its numbers change, because the strip draws them on every frame.</summary>
    private readonly NumberLabel level = new(), health = new(), reviving = new(), mana = new(), experience = new();

    protected override void DrawSelf(SpriteBatch sb)
    {
        Rectangle r = GetDimensions().ToRectangle();
        var companion = CompanionNPC.Instance;
        Rectangle portrait = Portrait(r);
        DrawCardPrimitives.Fill(sb, portrait, Sunk);
        DrawDronePortrait.Draw(sb, new Rectangle(portrait.Center.X - 24, portrait.Center.Y - 24, 48, 48));

        int x = r.X + (int)NameLeft;
        string name = companion == null || string.IsNullOrWhiteSpace(companion.NPC.GivenName) ? "Companion" : companion.NPC.GivenName;
        var save = Main.LocalPlayer.GetModPlayer<CompanionPlayer>();
        float nameWidth = FontAssets.MouseText.Value.MeasureString(name).X;
        DrawCardPrimitives.Text(sb, name, new Vector2(x, r.Y + 1), Color.White, 1f);
        if (companion != null)
            DrawCardPrimitives.Text(sb, level.Of(save.Experience.Level, 0, static (n, _) => $"Level {n}"), new Vector2(x + nameWidth + 10, r.Y + 5), Gold, .8f);

        float textHeight = FontAssets.MouseText.Value.MeasureString("0").Y * ReadingScale;
        for (int i = 0; i < 3; i++)
        {
            float fraction = 0;
            string reading = "";
            Color fill = i == 0 ? HealthFill : i == 1 ? ManaFill : ExperienceFill;
            if (companion != null)
            {
                switch (i)
                {
                    // Downed, the health bar is grey and fills with revival, the same reading the notch gives.
                    case 0 when companion.IsDowned:
                        fraction = Math.Clamp(companion.RevivePercent / 100f, 0, 1);
                        reading = reviving.Of(companion.RevivePercent, 0, static (percent, _) => $"Reviving {percent}%");
                        fill = DownedFill;
                        break;
                    case 0:
                        fraction = Math.Clamp((float)companion.NPC.life / Math.Max(1, companion.NPC.lifeMax), 0, 1);
                        reading = health.Of(companion.NPC.life, companion.NPC.lifeMax, static (life, max) => $"{Number(life)} / {Number(max)} HP");
                        break;
                    case 1:
                        fraction = companion.Mana.Fraction;
                        reading = mana.Of((int)MathF.Round(companion.Mana.Current), companion.Mana.Max, static (current, max) => $"{Number(current)} / {Number(max)} MP");
                        break;
                    default:
                        fraction = save.Experience.Fraction;
                        reading = experience.Of(save.Experience.IntoLevel, save.Experience.NeededNow, static (into, needed) => $"{Number(into)} / {Number(needed)} XP");
                        break;
                }
            }
            Rectangle bar = Bar(r, i);
            DrawCardPrimitives.Fill(sb, bar, BarTrack);
            DrawCardPrimitives.Fill(sb, bar with { Width = (int)(bar.Width * fraction) }, fill);
            DrawCardPrimitives.Text(sb, reading, new Vector2(bar.Right + ReadingGap, bar.Center.Y - textHeight / 2 + 2), fill, ReadingScale);
        }
    }
}
