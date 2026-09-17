#nullable enable

using Microsoft.Xna.Framework;
using AICompanion.Companion.Brain.Infrastructure.Observation;

namespace AICompanion.Companion.Brain.Infrastructure.Position;

/// <summary>
/// What the positioner answers about one proposed firing stand: where, the reach sense's three-valued
/// verdict, the ticks to travel there from the body, the predicted harm at the stand and sampled along
/// the travel line as shares of the companion's life, whether the stand lies in the activity's
/// allowance, and why.
/// </summary>
public readonly record struct StandVerdict(Vector2 Stand, ReachVerdict Reach, float TravelTicks,
    float HarmAtStand, float HarmAlongTravel, bool InAllowance, string Reason);
