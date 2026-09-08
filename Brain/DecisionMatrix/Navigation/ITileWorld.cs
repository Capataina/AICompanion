#nullable enable

namespace AICompanion.Brain.DecisionMatrix.Navigation;

/// <summary>
/// The five facts about a tile the navigation core reads, behind an interface so the same
/// grid, search and physics run against the live world in the game and against a text
/// scenario in the replay tool. Nothing under Navigation/ except the game implementation
/// may touch a Terraria type, because the tool compiles the core without the game.
/// </summary>
public interface ITileWorld
{
    /// <summary>Inside the world with a margin; outside counts as solid.</summary>
    bool InWorld(int x, int y);

    /// <summary>A block the body collides with from every side: has a tile, not actuated, solid and not a platform.</summary>
    bool Solid(int x, int y);

    /// <summary>Something feet rest on: a solid block, or a platform or half block the body can also pass through.</summary>
    bool Support(int x, int y);

    /// <summary>Any liquid but lava.</summary>
    bool Water(int x, int y);

    bool Lava(int x, int y);
}
