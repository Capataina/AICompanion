#nullable enable
using Microsoft.Xna.Framework;
using AICompanion.Companion.Brain.BehaviourSelection;

namespace AICompanion.Companion.Brain.Behaviours.Companionship;

/// <summary>
/// Visible catch-up outside navigation. Like native pet AI_026, flight ignores terrain until a
/// clear landing near the owner exists. It supplies velocity to the sole motor, never a route
/// edge, so waiting or flying cannot enter the executed-route archive. No teleport branch.
/// </summary>
public sealed class RecoverDistantFollowing
{
    public bool Active { get; private set; }
    public string Reason { get; private set; } = "ordinary-travel";
    public int Flights { get; private set; }

    public bool Update(bool following, bool downed, bool playerAlive, Vector2 feet,
        Vector2 playerFeet, bool clearLanding)
    {
        if (downed || !playerAlive)
        {
            Active = false;
            Reason = downed ? "downed" : "player-unavailable";
            return false;
        }
        float distance = Vector2.Distance(feet, playerFeet);
        if (Active)
        {
            if (distance <= Weights.FollowRecoveryArrival && clearLanding)
            {
                Active = false;
                Reason = "arrived-clear";
            }
        }
        else if (following && distance > Weights.FollowRecoveryDistance)
        {
            Active = true;
            Flights++;
            Reason = "distant-following";
        }
        return Active;
    }

    public Vector2 Steer(Vector2 feet, Vector2 velocity, Vector2 playerFeet, Vector2 playerVelocity)
    {
        Vector2 delta = playerFeet - feet;
        float length = delta.Length();
        float speed = System.MathF.Max(Weights.FollowRecoverySpeed, playerVelocity.Length() + 2f);
        Vector2 desired = length > 1f ? delta / length * System.MathF.Min(speed, length * .12f) : Vector2.Zero;
        Vector2 change = desired - velocity;
        if (change.Length() > Weights.FollowRecoveryAcceleration)
            change = Vector2.Normalize(change) * Weights.FollowRecoveryAcceleration;
        return velocity + change;
    }
}
