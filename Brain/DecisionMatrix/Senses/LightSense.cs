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
/// the player stands in daylight must read the cave. Off screen the lighting engine
/// holds nothing, so every sample reads 0 there and a companion far away lights its
/// torch wherever it is; underground that is the wanted behaviour, and on the surface
/// at noon it is the price, and small.
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
        int excluded2 = ExcludeRadiusTiles * ExcludeRadiusTiles;

        float sum = 0f;
        int n = 0;
        for (int x = c.X - halfWidth; x <= c.X + halfWidth; x += SampleStrideTiles)
        {
            for (int y = c.Y - halfHeight; y <= c.Y + halfHeight; y += SampleStrideTiles)
            {
                int dx = x - c.X, dy = y - c.Y;
                if (dx * dx + dy * dy < excluded2)
                    continue;
                sum += Brightness(x, y);
                n++;
            }
        }
        Ambient = n > 0 ? sum / n : AtCompanion;
    }

    private static float Brightness(int x, int y)
        => WorldGen.InWorld(x, y, 1) ? MathHelper.Clamp(Lighting.Brightness(x, y), 0f, 1f) : 0f;
}
