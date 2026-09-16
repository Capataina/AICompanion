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
    /// <summary>The combat stance's firing stand: hover the point a plan's volley was aimed from. Nothing
    /// emits it yet — combat still asks for Guard and LineOfFire stands in this phase — so the positioner
    /// scores it nothing until the planner lands beside it.</summary>
    FireFrom,
    /// <summary>Exactly this point (an item, a trunk), no scoring.</summary>
    Exact,
    /// <summary>Stand still.</summary>
    Hold,
    /// <summary>Somewhere else the walker can reach from where it stands, the further the better: a stranded companion walking its pocket.</summary>
    Roam,
}

/// <summary>What the running action wants from the positioner. <paramref name="MeetingPlace"/> marks a
/// reunion whose anchor is already a hoverable place priced by the companion's own routes: the positioner
/// flies to that cell rather than scoring a region around it, because a different cell in the region can
/// have a different best route, and the price was paid for this one. <paramref name="WorkTile"/> marks an
/// exact request for a hover that a tool-reach proof chose for that tile, which is what lets the positioner
/// declare the tool's reach box as the request's success region; an exact request without it declares none.</summary>
public readonly record struct PositionRequest(RequestKind Kind, Vector2 Anchor, NPC? Target = null, bool MeetingPlace = false, Point? WorkTile = null)
{
    public static PositionRequest Hold => new(RequestKind.Hold, Vector2.Zero);
    public static PositionRequest ExactAt(Vector2 centre) => new(RequestKind.Exact, centre);
    /// <summary>A hover a tool-reach proof admitted for working <paramref name="workTile"/>.</summary>
    public static PositionRequest ExactAt(Vector2 centre, Point workTile) => new(RequestKind.Exact, centre, WorkTile: workTile);
}
