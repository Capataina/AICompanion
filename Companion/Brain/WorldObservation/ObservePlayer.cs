#nullable enable

using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using AICompanion.Companion.Brain.SharedMovementSystem;
using AICompanion.Companion.Brain.BehaviourSelection;

namespace AICompanion.Companion.Brain.WorldObservation;

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
    }
}
