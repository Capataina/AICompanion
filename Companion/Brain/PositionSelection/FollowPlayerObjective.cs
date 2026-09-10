#nullable enable

using System;
using Microsoft.Xna.Framework;
using AICompanion.Companion.Brain.BehaviourSelection;

namespace AICompanion.Companion.Brain.PositionSelection;

/// <summary>
/// The destination region that counts as being with the player. Following is complete only when
/// both axes are comfortable: a point on a nearby but different cave floor cannot satisfy the
/// same circular distance band. The player prediction leads a sustained climb or descent without
/// making a transient velocity spike the arrival condition.
/// </summary>
public readonly record struct FollowPlayerObjective(Vector2 PlayerFeet, Vector2 PredictedFeet)
{
    public float HorizontalComfort => Weights.FollowHorizontalComfort * PlayerIntegration.CompanionPreferences.Current.FollowComfortScale;
    public float VerticalComfort => Weights.FollowVerticalComfort * PlayerIntegration.CompanionPreferences.Current.FollowComfortScale;

    public float HorizontalGap(Vector2 feet) => MathF.Abs(feet.X - PlayerFeet.X);
    public float VerticalGap(Vector2 feet) => MathF.Abs(feet.Y - PlayerFeet.Y);

    /// <summary>
    /// A standing destination is useful inside either the predicted local region or the player's
    /// current local region. Prediction leads sustained motion; retaining the current region keeps
    /// a one-tick climb, fall or collision correction from moving every viable floor into a wall.
    /// </summary>
    public bool AcceptsDestination(Vector2 feet, bool locallyConnected)
    {
        // Navigator accepts any grounded pose in its arrival radius. Reserve that radius here
        // so reaching a legal destination cannot leave the original follow request unsatisfied.
        float horizontal = MathF.Max(0f, HorizontalComfort - SharedMovementSystem.Navigator.ArriveDistance);
        float vertical = MathF.Max(0f, VerticalComfort - SharedMovementSystem.Navigator.ArriveDistance);
        bool nearPrediction = MathF.Abs(feet.X - PredictedFeet.X) <= horizontal
            && MathF.Abs(feet.Y - PredictedFeet.Y) <= vertical;
        bool nearPlayer = HorizontalGap(feet) <= horizontal && VerticalGap(feet) <= vertical;
        return locallyConnected && (nearPrediction || nearPlayer);
    }

    /// <summary>Arrival is measured against the player's current body, not an old route waypoint.</summary>
    public bool IsSatisfied(Vector2 feet, bool locallyConnected)
        => HorizontalGap(feet) <= HorizontalComfort && VerticalGap(feet) <= VerticalComfort && locallyConnected;

    public string Reason(Vector2 feet, bool locallyConnected)
        => IsSatisfied(feet, locallyConnected) ? "follow-objective-satisfied"
            : VerticalGap(feet) > VerticalComfort ? "follow-vertical-gap"
            : HorizontalGap(feet) > HorizontalComfort ? "follow-horizontal-gap"
            : "follow-local-connection";
}
