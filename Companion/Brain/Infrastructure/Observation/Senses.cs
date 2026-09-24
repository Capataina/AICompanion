#nullable enable

using Terraria;

namespace AICompanion.Companion.Brain.Infrastructure.Observation;

/// <summary>
/// The world model. Rebuilt every tick from the game, read by every part of the brain,
/// and never a decision maker: it says what is, and derives the numbers the course
/// prices against (danger, horizon). Anything the brain wants to know about the world
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
    /// <summary>Where the body can fly to. Refreshed by the positioner's resolve rather than by
    /// <see cref="Update"/>, because where the flood is rooted and when a replacement takes over is
    /// decided on the rescore, whose region has to hold still for the candidates scored against it.</summary>
    public readonly ReachSense Reach = new();
    public readonly CompanionSense Self = new();
    public readonly ObserveProjectiles Projectiles = new();
    public readonly EncounterSense Encounter = new();

    public NPC Companion { get; private set; } = null!;
    public Terraria.Player PlayerEntity { get; private set; } = null!;
    public int Tick { get; private set; }

    /// <summary>Install the tick a snapshot was written at, after the restore's own update: the replayed search
    /// draws the sampler at the recorded tick, prices fire ticks against it, and reads deferral waits in it.</summary>
    public void AssumeTick(int tick) => Tick = tick;

    public float DistanceToPlayer { get; private set; }

    /// <summary>Applied after weapons inspect this tick's threats; infinity means no useful intervention was observed.</summary>
    public void SetInterventionEstimate(float ticks) => Threats.SetInterventionEstimate(ticks);

    public void Update(NPC companion, Terraria.Player player)
    {
        Tick++;
        Companion = companion;
        PlayerEntity = player;
        using (Diagnostics.BrainSections.Enter(PlayerSection)) Player.Update(player, companion);
        // Straight after the player, because it reads his freshly observed intent and every later
        // sense and every consumer this tick must see one region rather than two.
        using (Diagnostics.BrainSections.Enter(IntentSection)) Intent.Update(companion, Player);
        using (Diagnostics.BrainSections.Enter(ThreatsSection)) Threats.Update(player, companion);
        using (Diagnostics.BrainSections.Enter(EncounterSection)) Encounter.Update(player, Threats);
        using (Diagnostics.BrainSections.Enter(ProjectilesSection)) Projectiles.Update(companion);
        using (Diagnostics.BrainSections.Enter(LootSection)) Loot.Update(companion, player);
        using (Diagnostics.BrainSections.Enter(LightSection)) Light.Update(companion, player);
        using (Diagnostics.BrainSections.Enter(SelfSection)) Self.Update(companion);
        DistanceToPlayer = Microsoft.Xna.Framework.Vector2.Distance(companion.Center, player.Center);
    }

    // One profiler section per sense, so a costly observation names the sense that paid for it.
    private static readonly int PlayerSection = Diagnostics.BrainSections.Register("player");
    private static readonly int IntentSection = Diagnostics.BrainSections.Register("intent");
    private static readonly int ThreatsSection = Diagnostics.BrainSections.Register("threats");
    private static readonly int EncounterSection = Diagnostics.BrainSections.Register("encounter");
    private static readonly int ProjectilesSection = Diagnostics.BrainSections.Register("projectiles");
    private static readonly int LootSection = Diagnostics.BrainSections.Register("loot");
    private static readonly int LightSection = Diagnostics.BrainSections.Register("light");
    private static readonly int SelfSection = Diagnostics.BrainSections.Register("self");
}
