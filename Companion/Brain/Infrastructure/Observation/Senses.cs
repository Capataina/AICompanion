#nullable enable

using Terraria;

namespace AICompanion.Companion.Brain.Infrastructure.Observation;

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
    /// <summary>Where the player is going, as a place. Every "how far from the player" question in the
    /// brain measures to this, so work near him is work near where he will be rather than where he was.</summary>
    public readonly PlayerIntentRegionSense Intent = new();
    /// <summary>Where the body can walk to. Refreshed by the positioner's resolve rather than by
    /// <see cref="Update"/>, because the flood's lava and one-way rules are set per request.</summary>
    public readonly ReachSense Reach = new();
    public readonly CompanionSense Self = new();
    public readonly ObserveProjectiles Projectiles = new();
    public readonly EncounterSense Encounter = new();

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
        // Straight after the player, because it reads his freshly observed intent and every later
        // sense and every consumer this tick must see one region rather than two.
        Intent.Update(companion, Player);
        Threats.Update(player, companion);
        Encounter.Update(player, Threats);
        Projectiles.Update(companion);
        Loot.Update(companion, player);
        Light.Update(companion, player);
        Self.Update(companion, breath);
        DistanceToPlayer = Microsoft.Xna.Framework.Vector2.Distance(companion.Center, player.Center);
    }
}
