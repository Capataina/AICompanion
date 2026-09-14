#nullable enable

using System;
using Microsoft.Xna.Framework;
using AICompanion.Companion.Brain.Infrastructure.Interactions;

namespace AICompanion.Companion.Brain.Infrastructure.Position;

/// <summary>What a request's destination was admitted to achieve, as a place a body can be inside or outside.</summary>
public enum SuccessRegionKind
{
    /// <summary>No destination is held: a hold, or an attack request with nothing to aim at.</summary>
    None,
    /// <summary>Following: the comfort box around the player's feet or the request's anchor, the two places <see cref="FollowPlayerObjective.AcceptsDestination"/> admits against.</summary>
    FollowComfort,
    /// <summary>A tool stand: the native reach box around the work tile its proof chose the stand for.</summary>
    ToolReach,
    /// <summary>An attack position admitted with a solved arc; the arc belongs to a moving target, so no box is declared.</summary>
    FiringPosition,
    /// <summary>A priced meeting place, walked to as a tile without acceptance, so no box is declared.</summary>
    MeetingPlace,
    /// <summary>The follow fallback's closest reachable tile: admitted to get the body there and no further, so its
    /// region is the navigator's own arrival radius around it. It declares one rather than nothing because a
    /// destination with no region can be arrived at without having achieved anything, which is how the same tile
    /// was handed back to a body already standing on it for hundreds of ticks.</summary>
    PartialProgress,
    /// <summary>An exact or roaming destination whose request carries no purpose geometry.</summary>
    Undeclared,
}

/// <summary>
/// The success region the positioner admitted its current destination against, snapshotted on the tick it
/// resolved it, so a record can judge a claimed arrival against the region the destination was chosen for
/// rather than against whatever the world looks like later. The references are the admission's own: the
/// player's feet and anchor the follow objective compared against, the reach the tool box used. Contains is
/// arithmetic only; it never asks the world, so reading it in diagnostics changes nothing.
/// </summary>
public readonly record struct SuccessRegion(SuccessRegionKind Kind, int AdmittedTick, int TerrainRevision, Vector2 Anchor,
    Vector2 PlayerFeet = default, Vector2 Comfort = default, Point? WorkTile = null, int ReachX = 0, int ReachY = 0)
{
    public static SuccessRegion None => new(SuccessRegionKind.None, 0, 0, Vector2.Zero);

    public static SuccessRegion Unscored(SuccessRegionKind kind, Vector2 anchor, int tick, int terrainRevision)
        => new(kind, tick, terrainRevision, anchor);

    public static SuccessRegion Follow(in FollowPlayerObjective objective, int tick, int terrainRevision)
        => new(SuccessRegionKind.FollowComfort, tick, terrainRevision, objective.PredictedFeet, objective.PlayerFeet,
            new Vector2(objective.HorizontalComfort, objective.VerticalComfort),
            ReachX: (int)MathF.Round(objective.AnchorHorizontalComfort),
            ReachY: (int)MathF.Round(objective.AnchorVerticalComfort));

    /// <summary>The fallback's own tile, with the navigator's arrival radius as its box: being there is the whole claim.</summary>
    public static SuccessRegion Partial(Vector2 destination, int tick, int terrainRevision)
        => new(SuccessRegionKind.PartialProgress, tick, terrainRevision, destination,
            Comfort: new Vector2(Infrastructure.Movement.Navigator.ArriveDistance, Infrastructure.Movement.Navigator.ArriveDistance));

    public static SuccessRegion ToolStand(Vector2 stand, Point tile, int tick, int terrainRevision)
        => new(SuccessRegionKind.ToolReach, tick, terrainRevision, stand, WorkTile: tile,
            ReachX: FindToolAccess.ReachX, ReachY: FindToolAccess.ReachY);

    /// <summary>Whether <paramref name="feet"/> lies inside the declared region, or null when the kind declares none.
    /// A follow destination is admitted with the navigator's arrival slack reserved inside the comfort box, so an
    /// arrival outside it names a destination that was never admitted; a tool box holds only the arithmetic half of
    /// reach, and a pose inside it can still lack a line to an exposed face.</summary>
    public bool? Contains(Vector2 feet) => Kind switch
    {
        SuccessRegionKind.FollowComfort => Near(feet, PlayerFeet) || Near(feet, Anchor, ReachX > 0 ? ReachX : Comfort.X, ReachY > 0 ? ReachY : Comfort.Y),
        SuccessRegionKind.ToolReach => WorkTile is Point tile && FindToolAccess.InReachBox(feet, tile, ReachX, ReachY),
        SuccessRegionKind.PartialProgress => Near(feet, Anchor),
        _ => null,
    };

    /// <summary>The recorded name of the kind, which SessionReport matches literally.</summary>
    public string Name => Kind switch
    {
        SuccessRegionKind.FollowComfort => "follow-comfort",
        SuccessRegionKind.ToolReach => "tool-reach",
        SuccessRegionKind.FiringPosition => "firing-position",
        SuccessRegionKind.MeetingPlace => "meeting-place",
        SuccessRegionKind.PartialProgress => "partial-progress",
        SuccessRegionKind.Undeclared => "undeclared",
        _ => "none",
    };

    private bool Near(Vector2 feet, Vector2 centre)
        => Near(feet, centre, Comfort.X, Comfort.Y);

    private static bool Near(Vector2 feet, Vector2 centre, float horizontal, float vertical)
        => MathF.Abs(feet.X - centre.X) <= horizontal && MathF.Abs(feet.Y - centre.Y) <= vertical;
}
