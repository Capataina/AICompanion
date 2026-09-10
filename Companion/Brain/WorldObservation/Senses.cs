#nullable enable

using Terraria;

namespace AICompanion.Companion.Brain.WorldObservation;

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
    public readonly LightSense Light = new();
    public readonly CompanionSense Self = new();
    public readonly ObserveProjectiles Projectiles = new();

    public NPC Companion { get; private set; } = null!;
    public Terraria.Player PlayerEntity { get; private set; } = null!;
    public int Tick { get; private set; }

    public float DistanceToPlayer { get; private set; }

    /// <summary>Applied after weapons inspect this tick's threats; infinity means no useful intervention was observed.</summary>
    public void SetInterventionEstimate(float ticks) => Threats.SetInterventionEstimate(ticks);

    public void Update(NPC companion, Terraria.Player player, global::AICompanion.Companion.CharacterBody.CompanionBreath breath)
    {
        Tick++;
        Companion = companion;
        PlayerEntity = player;
        Player.Update(player, companion);
        Threats.Update(player, companion);
        Projectiles.Update(companion);
        Loot.Update(companion, player);
        Light.Update(companion, player);
        Self.Update(companion, breath);
        DistanceToPlayer = Microsoft.Xna.Framework.Vector2.Distance(companion.Center, player.Center);
    }
}
