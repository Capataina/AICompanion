#nullable enable

namespace AICompanion.Companion.Brain.Infrastructure.Interactions.Firing;

/// <summary>
/// What a weapon's use looks like before anything is simulated: its launch speed, its projectile's box,
/// how long the pre-gate traces it, and the furthest a use is worth attempting. The learned arc this used
/// to carry is gone — flight is the weapon knowledge's fitted laws now, read through the simulator — and
/// what remains is the cheap reach check every forecast and stand gates on before paying for a sim. The
/// attack planner prices stands by simulated uses instead, so this struct goes with the positioner's
/// firing-stand scoring when that lands.
/// </summary>
public readonly record struct FlightModel(float Speed, int HitboxSize, int MaxFlightTicks, float Reach);
