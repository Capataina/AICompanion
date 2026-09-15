#nullable enable

namespace AICompanion.Companion.Brain.Infrastructure.Movement;

/// <summary>
/// The one reading of a tile the planner, the flood and the clearance field share: a tile is free for the orb when it is
/// not solid under the contact's rules. Every liquid is air to this body, so a wet tile is exactly as free as a dry one —
/// the owner ruled on 15 September 2026 that the companion's immunity to water, honey, lava and shimmer is built in rather
/// than a mastery unlock, because a capability an upgrade switches on would have to be tested with and without it after
/// every later change to the brain.
///
/// <para>Until then water and lava were walls here until an immunity opened them, which made the flood, every route and
/// the clearance field depend on a process-wide immunity setting that the brain tick copied in each tick. With nothing
/// left to configure, the rule is the contact's own, and every search that asks this class asks exactly what the body
/// collides with.</para>
/// </summary>
public static class OrbTerrain
{
    public static bool Solid(ITileWorld world, int x, int y) => CircleContact.Solid(world, x, y);

    public static bool Free(ITileWorld world, int x, int y) => !Solid(world, x, y);

    /// <summary>The wall predicate a search hands to the contact's swept test.</summary>
    public static bool Wall(ITileWorld world, int x, int y) => Solid(world, x, y);
}
