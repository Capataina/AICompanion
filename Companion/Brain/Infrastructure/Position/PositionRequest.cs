#nullable enable

using Microsoft.Xna.Framework;
using Terraria;

namespace AICompanion.Companion.Brain.Infrastructure.Position;

public enum RequestKind
{
    /// <summary>Stay in the band around the player, biased along their travel.</summary>
    WithPlayer,
    /// <summary>Close to the player with sight lines, between them and the threat where possible.</summary>
    Guard,
    /// <summary>A spot with a solved line of fire to the target.</summary>
    LineOfFire,
    /// <summary>Exactly this point (an item, a trunk), no scoring.</summary>
    Exact,
    /// <summary>Stand still.</summary>
    Hold,
    /// <summary>Somewhere else the walker can reach from where it stands, the further the better: a stranded companion walking its pocket.</summary>
    Roam,
}

/// <summary>What the running action wants from the positioner. <paramref name="MeetingPlace"/> marks a
/// reunion whose anchor is already a standable place priced by the companion's own routes: the positioner
/// walks to that tile rather than scoring a region around it, because a different tile in the region can
/// have a different best route, and the price was paid for this one. <paramref name="WorkTile"/> marks an
/// exact request for a stand that a tool-reach proof chose for that tile, which is what lets the positioner
/// declare the tool's reach box as the request's success region; an exact request without it declares none.</summary>
public readonly record struct PositionRequest(RequestKind Kind, Vector2 Anchor, NPC? Target = null, float JumpScale = 0f, bool MeetingPlace = false, Point? WorkTile = null)
{
    public static PositionRequest Hold => new(RequestKind.Hold, Vector2.Zero);
    public static PositionRequest ExactAt(Vector2 feet) => new(RequestKind.Exact, feet);
    /// <summary>A stand a tool-reach proof admitted for working <paramref name="workTile"/>. Only a standing
    /// proof qualifies: a hop take-off is by construction a pose that does not reach, so it declares no tile.</summary>
    public static PositionRequest ExactAt(Vector2 feet, Point workTile) => new(RequestKind.Exact, feet, WorkTile: workTile);
}
