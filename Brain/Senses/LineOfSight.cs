#nullable enable

using Microsoft.Xna.Framework;
using Terraria;

namespace AICompanion.Brain.Senses;

/// <summary>Thin names over the game's own sight tests, so the brain reads as prose.</summary>
public static class LineOfSight
{
    public static bool Between(Entity a, Entity b)
        => Collision.CanHitLine(a.position, a.width, a.height, b.position, b.width, b.height);

    public static bool Between(Vector2 from, Entity to)
        => Collision.CanHitLine(from, 1, 1, to.position, to.width, to.height);
}
