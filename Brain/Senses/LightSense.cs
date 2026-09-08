#nullable enable

using System;
using Microsoft.Xna.Framework;
using Terraria;

namespace AICompanion.Brain.Senses;

/// <summary>
/// How dark it is, measured so the companion's own torch cannot answer the question.
/// Three numbers: light at the player's tiles, light on a coarse grid over the screen
/// with a disc around the companion cut out (a torch's glow reaches roughly eight tiles,
/// the cut is ten), and light right at the companion for the overlay. The screen sample
/// is the one the torch decision uses, because it is the only one the torch cannot
/// brighten, and hysteresis on top of it removes the last way to flicker.
///
/// Off screen the lighting engine holds nothing, so every number reads 0 there; a
/// companion sent away underground therefore lights its torch, which is the wanted
/// behaviour, and one sent away across the surface at noon does too, which is the
/// price and is small.
/// </summary>
public sealed class LightSense
{
    private const int SampleStrideTiles = 4;
    private const int ExcludeRadiusTiles = 10;
    private const int RefreshTicks = 10;

    /// <summary>Mean brightness of the screen away from the companion, 0..1.</summary>
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

        int left = (int)(Main.screenPosition.X / 16f);
        int top = (int)(Main.screenPosition.Y / 16f);
        int right = left + Main.screenWidth / 16;
        int bottom = top + Main.screenHeight / 16;
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
        Ambient = n > 0 ? sum / n : AtPlayer;
    }

    private static float Brightness(int x, int y)
        => WorldGen.InWorld(x, y, 1) ? MathHelper.Clamp(Lighting.Brightness(x, y), 0f, 1f) : 0f;
}
