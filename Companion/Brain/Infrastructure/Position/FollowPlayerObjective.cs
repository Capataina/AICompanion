#nullable enable

using System;
using Microsoft.Xna.Framework;
using AICompanion.Companion.Brain.Infrastructure.Selection;

namespace AICompanion.Companion.Brain.Infrastructure.Position;

/// <summary>
/// The destination region that counts as being with the player. Following is complete only when
/// both axes are comfortable: a point on a nearby but different cave floor cannot satisfy the
/// same circular distance band. <paramref name="PredictedFeet"/> is the request's anchor: for reunion
/// that is the meeting place `ChooseMeetingPlace` priced on the player's journey, which may lie well
/// ahead of the player or on a route the companion reaches differently, while arrival is still
/// judged against the player's current body.
/// </summary>
public readonly record struct FollowPlayerObjective(Vector2 PlayerFeet, Vector2 PredictedFeet)
{
    public float HorizontalComfort => Weights.FollowHorizontalComfort * PlayerIntegration.CompanionPreferences.Current.FollowComfortScale;
    public float VerticalComfort => Weights.FollowVerticalComfort * PlayerIntegration.CompanionPreferences.Current.FollowComfortScale;
    /// <summary>The ahead-box grows with how far the meeting place sits from the player, from none beside them up to MeetingBoxGrowth at MeetingBoxGrowthDistance.</summary>
    public float AheadGrowth => 1f + Weights.MeetingBoxGrowth * MathF.Min(1f,
        Vector2.Distance(PlayerFeet, PredictedFeet) / MathF.Max(1f, Weights.MeetingBoxGrowthDistance));
    public float AnchorHorizontalComfort => HorizontalComfort * AheadGrowth;
    public float AnchorVerticalComfort => VerticalComfort * AheadGrowth;

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
        float horizontal = MathF.Max(0f, HorizontalComfort - Infrastructure.Movement.Navigator.ArriveDistance);
        float vertical = MathF.Max(0f, VerticalComfort - Infrastructure.Movement.Navigator.ArriveDistance);
        float aheadH = MathF.Max(0f, AnchorHorizontalComfort - Infrastructure.Movement.Navigator.ArriveDistance);
        float aheadV = MathF.Max(0f, AnchorVerticalComfort - Infrastructure.Movement.Navigator.ArriveDistance);
        bool nearPrediction = MathF.Abs(feet.X - PredictedFeet.X) <= aheadH
            && MathF.Abs(feet.Y - PredictedFeet.Y) <= aheadV;
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
