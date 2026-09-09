#nullable enable

using System;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using AICompanion.Brain.DecisionMatrix.Navigation;

namespace AICompanion.Brain.Work.Doors;

/// <summary>
/// Opens a door the body is walking into and closes it again once the body is through, which is
/// what a townsperson does and what the player does by hand.
///
/// The whole rule is the town NPC's own, read from the decompile (<c>AI_007_TownEntities</c>, the
/// block around the <c>closeDoor</c> flag), because the interesting part is not the decision but
/// the three-way attempt: <c>WorldGen.OpenDoor</c> takes a direction and returns whether the door
/// could actually swing that way, so the door swings the way the body is going if there is room,
/// swings inward if the far side is blocked, and only fails when both sides are blocked. Nothing
/// here has to know what is beside the door or measure any clearance — the game already answers
/// that, and asking it is a single call.
///
/// Two departures from the town NPC, both deliberate. There is no <c>Main.rand.Next(10)</c> gate:
/// a townsperson dithering at a doorway reads as character, and a companion failing to follow for
/// a few tenths of a second reads as broken. And there is no wait before the attempt, where the
/// town NPC counts <c>ai[2]</c> to 60 first, for the same reason.
///
/// Tall gates are the third case rather than an afterthought: they are a different tile with a
/// different verb (<c>ShiftTallGate</c>) and the same contract, so a companion that handled doors
/// and not gates would be stuck at exactly the doorway a player builds when they want the door to
/// stay out of the way.
///
/// The world changes without the tile hooks firing — <c>WorldGen.OpenDoor</c> neither kills nor
/// places through <c>KillTile</c> or <c>PlaceInWorld</c> — so the edge cache is told directly.
/// Without that the planner would keep the closed door's edges until they aged out, which is a
/// wall in the grid where the body has just made an opening.
/// </summary>
public sealed class DoorOpener
{
    /// <summary>The tall gate's closed and open tile ids, which have no TileID.Sets membership to ask instead.</summary>
    private const int TallGateClosed = TileID.TallGateClosed;
    private const int TallGateOpen = TileID.TallGateOpen;

    /// <summary>How far past the door, in tiles, the body must be before it is worth closing behind it.</summary>
    private const int PastDoorTiles = 2;

    /// <summary>How far from the door the companion gives up on ever closing it; it has walked away.</summary>
    private const int ForgetDoorTiles = 4;

    /// <summary>Slower than this and the body is not trying to go anywhere, so a door in front of it is scenery.</summary>
    private const float MovingSpeed = 0.1f;

    private bool holdsOpen;
    private int doorX, doorY;

    /// <summary>A new session or a new companion: no door is being held open by this one any more.</summary>
    public void Reset() => holdsOpen = false;

    /// <summary>
    /// One tick of door handling for a body facing <paramref name="npc"/>'s own direction. Called
    /// after the motor has run, so the direction is the one the brain asked for this tick.
    /// </summary>
    public void Tick(NPC npc)
    {
        CloseBehind(npc);
        if (MathF.Abs(npc.velocity.X) < MovingSpeed && !npc.collideX)
            return;

        Point feet = NavGrid.FeetTile(npc.Bottom);
        int ahead = feet.X + npc.direction;
        for (int row = feet.Y; row >= feet.Y - NavGrid.BodyHeightTiles + 1; row--)
        {
            Tile tile = Framing.GetTileSafely(ahead, row);
            if (!tile.HasTile)
                continue;
            if (TileLoader.IsClosedDoor(tile))
            {
                Open(ahead, row, npc.direction);
                return;
            }
            if (tile.TileType == TallGateClosed)
            {
                if (WorldGen.ShiftTallGate(ahead, row, closing: false))
                    Took(ahead, row);
                return;
            }
        }
    }

    /// <summary>
    /// The door swings the way the body is going, or inward when that side is blocked, and stays
    /// shut only when both sides are. The game's own return value is the clearance test.
    /// </summary>
    private void Open(int x, int y, int direction)
    {
        if (WorldGen.OpenDoor(x, y, direction) || WorldGen.OpenDoor(x, y, -direction))
            Took(x, y);
    }

    private void Took(int x, int y)
    {
        holdsOpen = true;
        doorX = x;
        doorY = y;
        // The grid has to hear about it: opening a door changes tiles through neither KillTile nor
        // PlaceInWorld, so nothing else would tell the edge cache that the wall it proved is gone.
        for (int row = y - 1; row <= y + 1; row++)
            AStar.TileChanged(x, row);
    }

    /// <summary>
    /// Shut it once the body is clear of the doorway, and give up on it once the body is far
    /// enough away that it is somebody else's door now — otherwise a companion that walked off
    /// mid-doorway would hold one open for the rest of the session.
    /// </summary>
    private void CloseBehind(NPC npc)
    {
        if (!holdsOpen)
            return;
        float column = npc.Center.X / 16f;
        float row = npc.Center.Y / 16f;
        if (MathF.Abs(column - doorX) > ForgetDoorTiles || MathF.Abs(row - doorY) > ForgetDoorTiles)
        {
            holdsOpen = false;
            return;
        }
        if (MathF.Abs(column - doorX) <= PastDoorTiles)
            return;

        Tile tile = Framing.GetTileSafely(doorX, doorY);
        bool closed = TileLoader.CloseDoorID(tile) >= 0
            ? WorldGen.CloseDoor(doorX, doorY)
            : tile.TileType == TallGateOpen && WorldGen.ShiftTallGate(doorX, doorY, closing: true);
        if (!closed)
            return;
        holdsOpen = false;
        for (int r = doorY - 1; r <= doorY + 1; r++)
            AStar.TileChanged(doorX, r);
    }
}
