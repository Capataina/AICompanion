#nullable enable

namespace AICompanion.Brain.SharedMovementSystem;

/// <summary>
/// The collision shape of one tile, as the game's own collision sees it. Slopes are named by
/// the triangle that is solid: a floor slope the body walks up is solid in its lower half, a
/// ceiling slope in its upper half. The numbers match the game's slope ids (1..4) so a live
/// tile converts by a cast, and the platform and half block are the two shapes the body can
/// stand on and still move through.
/// </summary>
public enum TileShape
{
    Air = 0,
    /// <summary>Solid in the lower-left triangle: the surface runs from the top-left corner down to the bottom-right, like a backslash. Game slope 1.</summary>
    SolidLowerLeft = 1,
    /// <summary>Solid in the lower-right triangle: the surface runs from the bottom-left corner up to the top-right, like a slash. Game slope 2.</summary>
    SolidLowerRight = 2,
    /// <summary>Solid in the upper-left triangle: a ceiling slope. Game slope 3.</summary>
    SolidUpperLeft = 3,
    /// <summary>Solid in the upper-right triangle: a ceiling slope. Game slope 4.</summary>
    SolidUpperRight = 4,
    /// <summary>A full block, collided with from every side.</summary>
    Solid = 5,
    /// <summary>Solid in its lower half only; the body stands on the middle of the tile.</summary>
    Half = 6,
    /// <summary>Stood on from above, passed through from every other direction and on purpose from above.</summary>
    Platform = 7,
}

/// <summary>
/// The facts about a tile the navigation core reads, behind an interface so the same grid,
/// search and physics run against the live world in the game and against a text scenario in
/// the replay tool. Nothing under Navigation/ except the game implementation may touch a
/// Terraria type, because the tool compiles the core without the game.
/// </summary>
public interface ITileWorld
{
    /// <summary>Changes whenever announced movement geometry changes; immutable fixtures use zero.</summary>
    int Revision => 0;
    /// <summary>Inside the world with a margin; outside counts as solid.</summary>
    bool InWorld(int x, int y);

    /// <summary>What the body collides with here: nothing, a full block, a half block, a slope or a platform. An actuated block is air.</summary>
    TileShape Shape(int x, int y);

    /// <summary>A tile can retain platform fall-through behaviour after hammering gives it a slope or half shape.</summary>
    bool PassThrough(int x, int y);

    /// <summary>Any liquid but lava.</summary>
    bool Water(int x, int y);

    bool Lava(int x, int y);
}

/// <summary>
/// An optional authoritative one-tick backend. The live Terraria adapter implements this with
/// the game's collision helpers; text scenarios use <see cref="BodyMotion"/>'s shape model.
/// Keeping the switch at this interface makes the caller's state/control contract identical in
/// both environments rather than selecting a second navigator at runtime.
/// </summary>
public interface IBodySimulationWorld
{
    BodyState Simulate(BodyState state, Controls controls, MovementCapabilities capabilities);
}
