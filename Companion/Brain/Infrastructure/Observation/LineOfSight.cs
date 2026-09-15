#nullable enable

using Microsoft.Xna.Framework;
using Terraria;

namespace AICompanion.Companion.Brain.Infrastructure.Observation;

/// <summary>
/// Thin names over the game's own sight test, so the brain reads as prose. The test is
/// <see cref="Collision.CanHit"/>, a single-tile walk between the two centres that refuses only a
/// closed one-tile slot, and not <see cref="Collision.CanHitLine"/>: that one is a beam three rows
/// tall on a horizontal line — it refuses if the row above *or* below the line is solid — which is
/// right for a three-tile player and wrong for a twenty-pixel body resting one row above a floor,
/// whose every horizontal line the floor itself then breaks. A slime beside a resting orb sits in
/// the orb's own row, so under the beam every ground enemy at the body's height read as unseen and
/// the companion's danger from it was halved. `../Interactions/FindToolAccess.cs` made the same
/// choice for tool reach for the same reason.
/// </summary>
public static class LineOfSight
{
    public static bool Between(Entity a, Entity b)
        => Collision.CanHit(a.position, a.width, a.height, b.position, b.width, b.height);

    public static bool Between(Vector2 from, Entity to)
        => Collision.CanHit(from, 1, 1, to.position, to.width, to.height);
}
