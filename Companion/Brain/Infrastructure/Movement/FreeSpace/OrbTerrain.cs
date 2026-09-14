#nullable enable

namespace AICompanion.Companion.Brain.Infrastructure.Movement;

/// <summary>Which liquids the body may pass through and touch unharmed. The mastery tree flips these;
/// the body carries them as two booleans and the brain tick copies them here every tick.</summary>
public readonly record struct LiquidImmunity(bool Water, bool Lava)
{
    public static readonly LiquidImmunity None = new(false, false);
}

/// <summary>
/// The one reading of a tile the planner, the flood and the clearance field share: a tile is free
/// for the orb when it is not solid under the contact's rules and not wet with a liquid the body
/// is not immune to. Water and lava are walls to every search until an immunity opens them, so
/// the same edit to <see cref="Immunity"/> changes the flood for every consumer at once; honey and
/// shimmer are never walls, because they only slow the body.
///
/// <para>This is the planner's wall and not the contact's: the contact pushes the body out of solid
/// tiles only, so a body knocked into water is hurt rather than ejected, and a route is what keeps
/// it out of the water in the first place.</para>
/// </summary>
public static class OrbTerrain
{
    /// <summary>The immunities every search runs under this tick. Set by the brain tick from the
    /// body's flags; a headless tool sets it for the scene it draws.</summary>
    public static LiquidImmunity Immunity { get; set; } = LiquidImmunity.None;

    public static bool Solid(ITileWorld world, int x, int y) => CircleContact.Solid(world, x, y);

    /// <summary>A wet tile the body may not enter under the given immunities.</summary>
    public static bool WetWall(ITileWorld world, int x, int y, LiquidImmunity immunity)
    {
        if (!world.InWorld(x, y) || world.LiquidAmount(x, y) == 0) return false;
        return world.LiquidKind(x, y) switch
        {
            0 => !immunity.Water,
            1 => !immunity.Lava,
            _ => false,
        };
    }

    public static bool Free(ITileWorld world, int x, int y) => Free(world, x, y, Immunity);

    public static bool Free(ITileWorld world, int x, int y, LiquidImmunity immunity)
        => !Solid(world, x, y) && !WetWall(world, x, y, immunity);

    /// <summary>The wall predicate a search hands to the contact's swept test: solid or forbidden wet.</summary>
    public static bool Wall(ITileWorld world, int x, int y) => !Free(world, x, y, Immunity);
}
