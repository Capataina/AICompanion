#nullable enable
using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace AICompanion.Companion.ProfileCard;

/// <summary>
/// The companion as the card pictures it: the drone, a rough symmetrical pixel disc of shell, core and four
/// crystals, drawn from code in sixteen art pixels. The rectangles are the mock's <c>#drone-art</c> SVG, which
/// was drawn from the concept sheet under <c>Assets/</c>; the world body is still the probe placeholder, and
/// only the card's pictures are the drone.
/// </summary>
public static class DrawDronePortrait
{
    public const int ArtSize = 16;

    private static readonly Color Pale = new(0xe6, 0xc8, 0xff), Crystal = new(0xa6, 0x4d, 0xff), ShellLight = new(0x3a, 0x37, 0x50),
        Shell = new(0x2a, 0x28, 0x38), ShellDark = new(0x21, 0x1f, 0x2c), Rim = new(0x8f, 0x8a, 0xa6), Core = new(0x7a, 0x2f, 0xd0),
        CoreLight = new(0xc8, 0x8d, 0xff), Glint = new(0xf3, 0xe6, 0xff);

    /// <summary>The core's colour, which the render fixture looks for to know the drone was drawn.</summary>
    public static Color CoreColour => Core;

    private static readonly (int X, int Y, int W, int H, Color C)[] Art =
    {
        (7, 0, 2, 1, Pale), (7, 1, 2, 2, Crystal), (7, 13, 2, 2, Crystal), (7, 15, 2, 1, Pale),
        (0, 7, 1, 2, Pale), (1, 7, 2, 2, Crystal), (13, 7, 2, 2, Crystal), (15, 7, 1, 2, Pale),
        (5, 3, 6, 1, ShellLight), (4, 4, 8, 1, ShellLight), (3, 5, 10, 6, Shell), (3, 5, 4, 2, ShellLight),
        (4, 11, 8, 1, ShellDark), (5, 12, 6, 1, ShellDark), (2, 6, 1, 4, Rim), (13, 6, 1, 4, Rim),
        (6, 6, 4, 4, Core), (7, 7, 2, 2, CoreLight), (7, 7, 1, 1, Glint),
    };

    /// <summary>Draw the drone centred in <paramref name="area"/> at the largest whole art-pixel size that fits.</summary>
    public static void Draw(SpriteBatch sb, Rectangle area)
    {
        int pixel = Math.Max(1, Math.Min(area.Width, area.Height) / ArtSize);
        int x0 = area.X + (area.Width - pixel * ArtSize) / 2, y0 = area.Y + (area.Height - pixel * ArtSize) / 2;
        foreach (var (x, y, w, h, c) in Art)
            DrawCardPrimitives.Fill(sb, new Rectangle(x0 + x * pixel, y0 + y * pixel, w * pixel, h * pixel), c);
    }
}
