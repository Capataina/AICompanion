#nullable enable

using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using AICompanion.Companion.Brain.Infrastructure.Movement;
using AICompanion.Companion.Brain.Infrastructure.Selection;

namespace AICompanion.Companion.Brain.Infrastructure.Observation;

/// <summary>
/// What the companion knows about the player: where they are and are going, whether
/// they are fighting, and whether they are really chopping. Travel intent is inferred
/// from bounded displacement history and local work, with explicit confidence.
/// </summary>
public sealed class PlayerSense
{
    public InferPlayerActivity Activity { get; } = new();

    public Vector2 Position { get; private set; }
    public Vector2 Bottom { get; private set; }
    public Vector2 Velocity { get; private set; }

    /// <summary>Confidence-weighted travel direction and pace in px/tick from recent displacement.</summary>
    public Vector2 Intent => Activity.Travel;
    public bool IsTravelling => Intent.LengthSquared() > Weights.PlayerIntentTravelSpeed * Weights.PlayerIntentTravelSpeed;
    public int TravelDirection => MathF.Sign(Intent.X);

    public float HealthFraction { get; private set; } = 1f;
    public bool IsDead { get; private set; }
    public bool IsAttacking { get; private set; }
    public bool IsChoppingTree { get; private set; }
    public Point? ChoppedTree { get; private set; }

    /// <summary>The ore tile and type the player hit within the last three quarters of a second, or null.</summary>
    public (Point Tile, int Type)? MinedOre { get; private set; }
    public bool CompanionCanSeePlayer { get; private set; }

    /// <summary>
    /// The tiles the player is asking the companion to vacate, in tile coordinates, or null: the tile a held solid block or
    /// wall is aimed at while the companion's body covers it, or the stretch of a one-body-tall passage ahead of a player
    /// walking into the companion. Held for a short window after it was last seen. It is evidence of interference and not a
    /// command: a weapon or a torch aimed at the companion is neither, and pointing a block at it from out of range is not.
    /// </summary>
    public Rectangle? Interference { get; private set; }
    /// <summary>Moves whenever a different footprint appears, so a held destination is reconsidered once rather than every tick.</summary>
    public int InterferenceRevision { get; private set; }
    private ulong interferenceUntil;

    /// <summary>A bounded, confidence-weighted continuation, not a known destination.</summary>
    public Vector2 Predict(int ticks) => Bottom + Intent * Math.Clamp(ticks, 0, Weights.PlayerIntentHistoryTicks);

    /// <summary>
    /// The player's feet tiles, oldest first, one entry per change of tile, up to TrailLength: the
    /// record of where a body the player's size actually got through, which every scenario dump
    /// carries so the replay tool can name the first trail tile the grid refuses.
    /// </summary>
    public IReadOnlyList<Point> Trail => trail;
    private readonly List<Point> trail = new();
    private const int TrailLength = 240;

    public void Update(Player player, NPC companion)
    {
        Position = player.Center;
        Bottom = player.Bottom;
        Velocity = player.velocity;
        // Only tiles the player stood in: the airborne part of a jump passes through tiles no
        // body can stand in, and the replay tool would name one of those as the missing link.
        Point feet = MovementQueries.FeetTile(player.Bottom);
        if (player.velocity.Y == 0f && (trail.Count == 0 || trail[^1] != feet))
        {
            trail.Add(feet);
            if (trail.Count > TrailLength)
                trail.RemoveAt(0);
        }
        IsDead = player.dead;
        HealthFraction = player.statLifeMax2 > 0 ? player.statLife / (float)player.statLifeMax2 : 1f;
        IsAttacking = player.itemAnimation > 0 && player.HeldItem.damage > 0;

        ChoppedTree = TileDamageWatcher.TreeHitByPlayerRecently();
        IsChoppingTree = ChoppedTree != null;
        MinedOre = TileDamageWatcher.OreHitByPlayerRecently();
        // These fields use negative values for no placement; valid IDs start at zero.
        bool placing = player.itemAnimation > 0 && (player.HeldItem.createTile >= TileID.Dirt || player.HeldItem.createWall >= WallID.None);
        Activity.Observe(Bottom, Velocity, IsChoppingTree || MinedOre != null || placing, IsDead, Main.GameUpdateCount);
        CompanionCanSeePlayer = LineOfSight.Between(companion, player);
        ObserveInterference(player, companion);
    }

    private void ObserveInterference(Player player, NPC companion)
    {
        Rectangle? seen = player.dead ? null : PlacementOnCompanion(player, companion) ?? PassageThroughCompanion(player, companion);
        ulong now = Main.GameUpdateCount;
        if (seen is Rectangle footprint)
        {
            if (Interference != footprint) InterferenceRevision++;
            Interference = footprint;
            interferenceUntil = now + (ulong)Weights.CourtesyEvidenceTicks;
        }
        else if (Interference != null && now >= interferenceUntil)
            Interference = null;
    }

    /// <summary>The tiles a body of this size covers with its feet at <paramref name="feet"/>, as a tile rectangle.</summary>
    public static Rectangle BodyTiles(Vector2 feet, int width, int height)
    {
        int left = (int)MathF.Floor((feet.X - width / 2f) / 16f);
        int right = (int)MathF.Floor((feet.X + width / 2f - 0.001f) / 16f);
        int top = (int)MathF.Floor((feet.Y - height) / 16f);
        int bottom = (int)MathF.Floor((feet.Y - 0.001f) / 16f);
        return new Rectangle(left, top, right - left + 1, bottom - top + 1);
    }

    private static Rectangle? PlacementOnCompanion(Player player, NPC companion)
    {
        Item held = player.HeldItem;
        if (held == null || held.IsAir)
            return null;
        // A solid block or a wall. createTile is zero for dirt, so the activity reading's "non-negative" counts every torch
        // as placement; a torch or a platform does not close the space a body stands in, so neither asks it to move.
        bool block = held.createTile >= 0 && held.createTile < Main.tileSolid.Length
            && Main.tileSolid[held.createTile] && !Main.tileSolidTop[held.createTile];
        bool wall = held.createWall > WallID.None;
        if (!block && !wall)
            return null;
        Point target = new(Player.tileTargetX, Player.tileTargetY);
        Point at = player.Center.ToTileCoordinates();
        if (Math.Abs(target.X - at.X) > Player.tileRangeX + held.tileBoost + player.blockRange
            || Math.Abs(target.Y - at.Y) > Player.tileRangeY + held.tileBoost + player.blockRange)
            return null;
        return BodyTiles(companion.Bottom, companion.width, companion.height).Contains(target)
            ? new Rectangle(target.X, target.Y, 1, 1) : null;
    }

    private static Rectangle? PassageThroughCompanion(Player player, NPC companion)
    {
        if (player.velocity.Y != 0f || MathF.Abs(player.velocity.X) < Weights.PlayerIntentTravelSpeed)
            return null;
        int dir = Math.Sign(player.velocity.X);
        // `from` is the lowest row the player's body fills, never the floor his feet rest on: `FeetTile` steps one
        // pixel up before flooring, because a standing body's feet sit exactly on the boundary and the floor row is
        // one no body is in. Floored onto the floor instead, the roof test below landed inside the body, this sense
        // never fired in the one scene it exists for, and a companion resting in a one-body-tall corridor was still
        // in the way seventeen ticks after a walking player reached it. The orb's cell is the one its centre is in.
        Point from = MovementQueries.FeetTile(player.Bottom), to = MovementQueries.Tile(companion.Center);
        if (Math.Abs(from.Y - to.Y) > 1 || Math.Abs(to.X - from.X) > Weights.CourtesyPassageTiles)
            return null;
        // One body tall: a roof directly over a standing body, so the two cannot pass by jumping. The footprint is the passage
        // the player is about to walk, measured from the player while the roof continues ahead, bounded. It is not measured
        // from the companion: anchored there, every step the companion took moved the footprint's edge with it, and the spot
        // just past that edge was still in the player's path. It holds wherever the companion is on that floor nearby, behind
        // as well as ahead, so reunion's meeting place on the journey is not chosen inside the passage either.
        int roof = from.Y - PlayerHeightTiles;
        if (!MovementQueries.IsBlock(from.X, roof))
            return null;
        int end = from.X;
        for (int step = 0; step < Weights.CourtesyPassageTiles && MovementQueries.IsBlock(end + dir, roof); step++)
            end += dir;
        if (end == from.X)
            return null;
        int left = Math.Min(from.X, end), right = Math.Max(from.X, end);
        return new Rectangle(left, roof + 1, right - left + 1, PlayerHeightTiles);
    }

    /// <summary>The player's own body is 42 pixels tall, three tile rows, which is the passage height a one-body-tall roof is measured against.</summary>
    private const int PlayerHeightTiles = 3;
}
