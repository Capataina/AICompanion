#nullable enable

using System;
using Microsoft.Xna.Framework;
using Terraria;

namespace AICompanion.Brain.DecisionMatrix.Senses;

/// <summary>
/// How dark it is where the companion is, measured so its own torch cannot answer the
/// question. Three numbers: light at the player's tile, light at the companion's tile
/// (which includes its own torch, so it is for the overlay only), and <see cref="Ambient"/>,
/// the mean over a coarse grid of a screen-sized window centred on the companion with a
/// disc around the companion cut out that is wider than a torch's glow. Ambient is the
/// one the torch decision reads, because it is the only one the torch cannot brighten,
/// and hysteresis on top of it removes the last way to flicker.
///
/// The window follows the companion, not the camera: a companion sent into a cave while
/// the player stands in daylight must read the cave. The lighting engine only holds
/// values for the visible screen, and reads 0 outside it, so the window is clipped to
/// the screen before averaging; otherwise a companion standing near the screen edge
/// would count the unlit outside as black and light a torch in a place that is dim, not
/// dark. When no sample is left the companion is fully off screen, ambient reads 0 and
/// a companion far away lights its torch wherever it is; underground that is the wanted
/// behaviour, and on the surface at noon it is the price, and small.
/// </summary>
public sealed class LightSense
{
    private const int SampleStrideTiles = 4;
    private const int ExcludeRadiusTiles = 10;
    private const int RefreshTicks = 10;

    /// <summary>Mean brightness around the companion, away from its own glow, 0..1.</summary>
    public float Ambient { get; private set; } = 1f;

    /// <summary>Brightness at the player's tile, 0..1.</summary>
    public float AtPlayer { get; private set; } = 1f;

    /// <summary>Brightness at the companion's tile, 0..1; includes its own torch, shown for the overlay only.</summary>
    public float AtCompanion { get; private set; } = 1f;

    private int sinceRefresh = RefreshTicks;

    public void Update(NPC companion, Player player)
    {
        if (++sinceRefresh < RefreshTicks)
            return;
        sinceRefresh = 0;

        Point c = companion.Center.ToTileCoordinates();
        Point p = player.Center.ToTileCoordinates();
        AtCompanion = Brightness(c.X, c.Y);
        AtPlayer = Brightness(p.X, p.Y);

        int halfWidth = Main.screenWidth / 32;
        int halfHeight = Main.screenHeight / 32;
        int screenLeft = (int)(Main.screenPosition.X / 16f);
        int screenTop = (int)(Main.screenPosition.Y / 16f);
        int screenRight = screenLeft + Main.screenWidth / 16;
        int screenBottom = screenTop + Main.screenHeight / 16;
        int left = Math.Max(c.X - halfWidth, screenLeft);
        int top = Math.Max(c.Y - halfHeight, screenTop);
        int right = Math.Min(c.X + halfWidth, screenRight);
        int bottom = Math.Min(c.Y + halfHeight, screenBottom);
        int excluded2 = ExcludeRadiusTiles * ExcludeRadiusTiles;

        float sum = 0f;
        int n = 0;
        for (int x = left; x <= right; x += SampleStrideTiles)
        {
            for (int y = top; y <= bottom; y += SampleStrideTiles)
            {
                int dx = x - c.X, dy = y - c.Y;
                if (dx * dx + dy * dy < excluded2)
                    continue;
                sum += Brightness(x, y);
                n++;
            }
        }
        // No sample inside the screen means the companion is off screen, where the engine
        // reads 0 anyway; the window that remains is its own dark or lit neighbourhood.
        Ambient = n > 0 ? sum / n : 0f;
    }

    private static float Brightness(int x, int y)
        => WorldGen.InWorld(x, y, 1) ? MathHelper.Clamp(Lighting.Brightness(x, y), 0f, 1f) : 0f;
}
