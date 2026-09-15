#nullable enable

using System;
using Microsoft.Xna.Framework;

namespace AICompanion.Companion.Brain.Infrastructure.Movement;

/// <summary>
/// The orb at one tick, in the terms the steering and every consumer read: where its centre is, how fast it moves, and
/// whether the engine is holding it still against its own velocity. The motor builds one from the NPC every tick and a
/// headless tool builds one from a pose, which is what lets the navigator run against either. It carries no liquid:
/// every liquid is air to this body, so nothing that steers it has a use for which one it is in.
/// </summary>
public readonly record struct OrbState(Vector2 Centre, Vector2 Velocity, bool Pinned = false)
{
    /// <summary>The tile the centre is in.</summary>
    public Point Tile => new((int)MathF.Floor(Centre.X / 16f), (int)MathF.Floor(Centre.Y / 16f));

    /// <summary>A body at rest at a point, the state every headless run starts from.</summary>
    public static OrbState Resting(Vector2 centre) => new(centre, Vector2.Zero);
}
