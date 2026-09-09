#nullable enable

using Microsoft.Xna.Framework;
using Terraria;

namespace AICompanion.Companion.Brain.PositionSelection;

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
    /// <summary>Away from close threats while keeping the player near.</summary>
    Retreat,
    /// <summary>Stand still.</summary>
    Hold,
    /// <summary>Somewhere else the walker can reach from where it stands, the further the better: a stranded companion walking its pocket.</summary>
    Roam,
}

/// <summary>What the running action wants from the positioner.</summary>
public readonly record struct PositionRequest(RequestKind Kind, Vector2 Anchor, NPC? Target = null, float JumpScale = 0f)
{
    public static PositionRequest Hold => new(RequestKind.Hold, Vector2.Zero);
    public static PositionRequest ExactAt(Vector2 feet) => new(RequestKind.Exact, feet);
}
