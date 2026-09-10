#nullable enable

using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using AICompanion.Companion.Brain.SharedMovementSystem;

namespace AICompanion.Companion.Brain.WorldObservation;

/// <summary>
/// What the companion knows about the player: where they are and are going, whether
/// they are fighting, and whether they are really chopping. Travel intent is velocity
/// smoothed over about three seconds that decays over about two after a stop, so a
/// pause in exploration does not read as a change of mind.
/// </summary>
public sealed class PlayerSense
{
    private const float IntentSmoothing = 0.02f;   // ~3 s to converge at 60 Hz
    private const float IntentDecayWhenStill = 0.985f; // ~2 s to fade

    public Vector2 Position { get; private set; }
    public Vector2 Bottom { get; private set; }
    public Vector2 Velocity { get; private set; }

    /// <summary>Smoothed travel direction and pace in px/tick; near zero when the player is not going anywhere.</summary>
    public Vector2 Intent { get; private set; }
    public bool IsTravelling => Intent.LengthSquared() > 1.2f * 1.2f;
    public int TravelDirection => MathF.Sign(Intent.X);

    public float HealthFraction { get; private set; } = 1f;
    public bool IsDead { get; private set; }
    public bool IsAttacking { get; private set; }
    public bool IsChoppingTree { get; private set; }
    public Point? ChoppedTree { get; private set; }

    /// <summary>The ore tile and type the player hit within the last three quarters of a second, or null.</summary>
    public (Point Tile, int Type)? MinedOre { get; private set; }
    public bool CompanionCanSeePlayer { get; private set; }

    /// <summary>Where the player will be if they keep their intent for <paramref name="ticks"/>.</summary>
    public Vector2 Predict(int ticks) => Bottom + Intent * ticks;

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

        Vector2 moving = player.velocity;
        if (moving.LengthSquared() > 0.5f * 0.5f)
            Intent = Vector2.Lerp(Intent, moving, IntentSmoothing * 3f);
        else
            Intent *= IntentDecayWhenStill;

        ChoppedTree = TileDamageWatcher.TreeHitByPlayerRecently();
        IsChoppingTree = ChoppedTree != null;
        MinedOre = TileDamageWatcher.OreHitByPlayerRecently();
        CompanionCanSeePlayer = LineOfSight.Between(companion, player);
    }
}
