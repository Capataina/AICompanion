#nullable enable
using Microsoft.Xna.Framework;
using AICompanion.Companion.Brain.Infrastructure.Position;
using AICompanion.Companion.Brain.Infrastructure.Selection;

namespace AICompanion.Companion.Brain.SharedBehaviours.Recovery;

/// <summary>
/// Visible catch-up outside navigation. Like native pet AI_026, flight ignores terrain until a
/// clear landing near the owner exists. It supplies velocity to the sole motor, never a route
/// edge, so waiting or flying cannot enter the executed-route archive. No teleport branch.
/// </summary>
public sealed class RecoverDistantCompanion
{
    public bool Active { get; private set; }
    public string Reason { get; private set; } = "ordinary-travel";
    public int Flights { get; private set; }

    /// <summary>
    /// Whether this tick's decision is an explicit reunion, which is the only thing that may start
    /// continuous recovery flight. Three conditions, and each is a rule rather than a guard:
    ///
    /// The request must be <see cref="RequestKind.WithPlayer"/>, never an executor's class and never a
    /// work destination that happens to sit near the player — so combat, mining and lighting cannot
    /// start it however far from him they lead.
    ///
    /// The hands must be free. A tool mid-job is work in progress, and flying home through it would
    /// abandon a swing the activity still believes it is taking.
    ///
    /// The decision must be settled. Every tick inside a running decision asks for companionship,
    /// because that is what the body would be doing anyway, so without this the brain starts flying
    /// home whenever it is merely thinking — the walker's own law that a fallback triggered by the
    /// absence of the ordinary path's precondition fires hardest while the planner is still working.
    /// Companionship chosen *as the answer* still admits it.
    ///
    /// It is a named predicate rather than an expression inside the tick because that is the only way
    /// it can be checked without a whole scene: it used to be tested by installing a stub activity in
    /// the chooser's list and reading back the request kind, and the course brain took that lever away
    /// — the request comes from the published course now, and an empty course asks for companionship,
    /// so every arm of that sweep read as reunion whatever it installed.
    /// </summary>
    public static bool ReunionRequested(RequestKind kind, bool handsBusy, bool decisionSettled)
        => kind == RequestKind.WithPlayer && !handsBusy && decisionSettled;

    public bool Update(bool reunionRequested, bool downed, bool playerAlive, Vector2 feet,
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
        else if (reunionRequested && distance > PlayerIntegration.CompanionPreferences.Current.RecoveryRadius)
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
