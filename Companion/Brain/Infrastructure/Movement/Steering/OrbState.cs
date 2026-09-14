#nullable enable

using System;
using Microsoft.Xna.Framework;

namespace AICompanion.Companion.Brain.Infrastructure.Movement;

/// <summary>
/// The orb at one tick, in the terms the steering and every consumer read: where its centre is,
/// how fast it moves, which liquid it is touching, and whether the engine is holding it still
/// against its own velocity. The motor builds one from the NPC every tick and a headless tool
/// builds one from a pose, which is what lets the navigator run against either.
/// </summary>
public readonly record struct OrbState(Vector2 Centre, Vector2 Velocity, int LiquidKind = -1, bool Pinned = false)
{
    /// <summary>The body touches no liquid at all this tick.</summary>
    public bool Dry => LiquidKind < 0;
    public bool InWater => LiquidKind == 0;
    public bool InLava => LiquidKind == 1;
    public bool InHoney => LiquidKind == 2;
    public bool InShimmer => LiquidKind == 3;

    /// <summary>
    /// The body holds a velocity and is not moving, which a body the engine integrates cannot do,
    /// so something is writing the position back underneath it. The steering reads it as a body
    /// that cannot act, which is the one state where standing still is not the body's own choice.
    /// </summary>
    public bool CannotAct => Pinned;

    /// <summary>The tile the centre is in.</summary>
    public Point Tile => new((int)MathF.Floor(Centre.X / 16f), (int)MathF.Floor(Centre.Y / 16f));

    /// <summary>A body at rest at a point, the state every headless run starts from.</summary>
    public static OrbState Resting(Vector2 centre) => new(centre, Vector2.Zero);
}
