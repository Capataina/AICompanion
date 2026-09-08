#nullable enable

using Terraria;

namespace AICompanion.Brain.Senses;

/// <summary>
/// The world model. Rebuilt every tick from the game, read by every part of the brain,
/// and never a decision maker: it says what is, and derives the numbers the chooser
/// scores against (danger, horizon). Anything the brain wants to know about the world
/// is added here, once, so five actions never compute it five ways.
/// </summary>
public sealed class Senses
{
    public readonly PlayerSense Player = new();
    public readonly ThreatSense Threats = new();
    public readonly LootSense Loot = new();

    public NPC Companion { get; private set; } = null!;
    public Terraria.Player PlayerEntity { get; private set; } = null!;
    public int Tick { get; private set; }

    public float DistanceToPlayer { get; private set; }

    public void Update(NPC companion, Terraria.Player player)
    {
        Tick++;
        Companion = companion;
        PlayerEntity = player;
        Player.Update(player, companion);
        Threats.Update(player, companion);
        Loot.Update(companion, player);
        DistanceToPlayer = Microsoft.Xna.Framework.Vector2.Distance(companion.Center, player.Center);
    }
}
