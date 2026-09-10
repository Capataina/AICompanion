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
/// Every hostile that matters, and the two numbers the brain leans on: the player's
/// danger and the safety horizon. Player and companion danger each use reachability
/// to their own body. Reachability and speed history are cached per NPC slot and refreshed on a
/// stagger so the expensive searches never all run in one tick.
/// </summary>
public sealed class ThreatSense
{
    private const int ReachabilityRefreshTicks = 60;
    private const float SpeedDecay = 0.995f;

    private sealed class Memory
    {
        public int Type;
        public int Generation = -1;
        public float PeakSpeed;
        public bool CanReachPlayer = true;
        public bool CanReachCompanion = true;
        public Point From, PlayerTarget, CompanionTarget;
        public ITileWorld? World;
        public int Revision;
        public MovementClass Class;
        public int ReachableCheckedAt = -1000;
    }

    private readonly Memory[] memory = new Memory[Main.maxNPCs];
    private int tick;

    public readonly List<ThreatRecord> Threats = new();

    /// <summary>
    /// 0..1, how much reachable danger the player is under. It is a combination of every threat
    /// rather than the worst of them: taken as a maximum, six zombies scored exactly like one
    /// zombie, so nothing could ever tell a crowd from a straggler and the companion kept walking
    /// beside the player holding a torch while a mob closed in (2026-09-09, mean 5.8 threats at a
    /// mean danger of 0.106). Threats combine the way independent hazards do — the chance that at
    /// least one of them lands — so the worst one still dominates, another of the same size adds
    /// less than the first did, and the result stays inside 0..1 without a cap that flattens.
    /// </summary>
    public float PlayerDanger { get; private set; }

    /// <summary>
    /// The same quantity measured about the companion instead of the player, which is the sense
    /// it never had. Every danger term in the brain used to be PlayerDanger, so a companion that
    /// walked away from the player was in a world with no danger in it at all — the player it left
    /// behind was safe by definition, which made hunting score *higher* the further it strayed.
    /// It hunted 84 tiles out, took five hits in five seconds and went down with danger reading
    /// 0.00 on every one of them (2026-09-09, ticks 2753 to 3082).
    /// </summary>
    public float CompanionDanger { get; private set; }

    /// <summary>The companion is in enough trouble to break off what it is doing and get clear.</summary>
    public bool CompanionInTrouble => CompanionDanger > 0.45f;

    /// <summary>Ticks the companion may stay away before the player is at risk; float.MaxValue when nothing threatens.</summary>
    public float Horizon { get; private set; } = float.MaxValue;
    public float ProtectionUrgency { get; private set; }
    public float InterventionTicks { get; private set; } = float.PositiveInfinity;

    public bool PlayerIsSafe => PlayerDanger < 0.25f;
    public ThreatRecord? MostUrgent { get; private set; }
    public ThreatRecord? MostUrgentToCompanion { get; private set; }

    public void Update(Player player, NPC companion)
    {
        tick++;
        Threats.Clear();
        PlayerDanger = 0f;
        CompanionDanger = 0f;
        Horizon = float.MaxValue;
        ProtectionUrgency = 0f;
        MostUrgent = null;
        MostUrgentToCompanion = null;
        // Danger is accumulated as the survival product (the chance nothing lands) and inverted at
        // the end, which is what makes several threats add up while one big one still dominates.
        float playerMiss = 1f, companionMiss = 1f;

        Point playerFeet = MovementQueries.FeetTile(player.Bottom);
        Point companionFeet = MovementQueries.FeetTile(companion.Bottom);

        foreach (NPC npc in Main.ActiveNPCs)
        {
            int projectileDamage = HostileAttackSources.RecentDamage(npc);
            if (npc.friendly || npc.life <= 0 || (npc.damage <= 0 && projectileDamage <= 0) || npc.CountsAsACritter)
                continue;

            PredictObservedMotion.Observe(npc);
            Memory mem = memory[npc.whoAmI] ??= new Memory();
            if (mem.Type != npc.type || mem.Generation != HostileAttackSources.Generation(npc))
            {
                mem.Generation = HostileAttackSources.Generation(npc);
                mem.Type = npc.type;
                mem.PeakSpeed = 1f;
                mem.CanReachPlayer = mem.CanReachCompanion = true;
                mem.ReachableCheckedAt = -1000;
            }

            MovementClass cls = npc.noTileCollide ? MovementClass.Phaser : (npc.noGravity ? MovementClass.Flyer : MovementClass.Walker);

            // A walker's approach speed is horizontal; its fall speed after a hop is not how fast it closes.
            float speed = cls == MovementClass.Walker ? MathF.Abs(npc.velocity.X) : npc.velocity.Length();
            mem.PeakSpeed = MathF.Max(mem.PeakSpeed * SpeedDecay, speed);

            Point from = cls == MovementClass.Walker ? MovementQueries.FeetTile(npc.Bottom) : npc.Center.ToTileCoordinates();
            Point playerTarget = cls == MovementClass.Walker ? playerFeet : player.Center.ToTileCoordinates();
            Point companionTarget = cls == MovementClass.Walker ? companionFeet : companion.Center.ToTileCoordinates();
            bool sourceChanged = mem.From != from || mem.World != MovementQueries.World
                || mem.Revision != MovementQueries.World.Revision || mem.Class != cls;
            // A negative result only describes the positions and terrain that were searched.
            // Treat changed inputs as unknown (potential danger) until the budgeted refresh.
            if (sourceChanged || mem.PlayerTarget != playerTarget) mem.CanReachPlayer = true;
            if (sourceChanged || mem.CompanionTarget != companionTarget) mem.CanReachCompanion = true;
            mem.From = from;
            mem.PlayerTarget = playerTarget;
            mem.CompanionTarget = companionTarget;
            mem.World = MovementQueries.World;
            mem.Revision = MovementQueries.World.Revision;
            mem.Class = cls;
            if (cls != MovementClass.Phaser && tick - mem.ReachableCheckedAt >= ReachabilityRefreshTicks && (tick + npc.whoAmI) % 4 == 0)
            {
                mem.CanReachPlayer = cls == MovementClass.Walker
                    ? MovementQueries.WalkerCanReach(from, playerTarget)
                    : MovementQueries.FlyerCanReach(from, playerTarget);
                mem.CanReachCompanion = playerTarget == companionTarget ? mem.CanReachPlayer
                    : cls == MovementClass.Walker
                        ? MovementQueries.WalkerCanReach(from, companionTarget)
                        : MovementQueries.FlyerCanReach(from, companionTarget);
                mem.ReachableCheckedAt = tick;
            }

            var rec = new ThreatRecord
            {
                Npc = npc,
                Class = cls,
                CanReachPlayer = cls == MovementClass.Phaser || mem.CanReachPlayer,
                CanReachCompanion = cls == MovementClass.Phaser || mem.CanReachCompanion,
                Shoots = projectileDamage > 0,
                ExpectedDamage = Math.Max(npc.damage, projectileDamage),
                IsBoss = npc.boss,
                ObservedSpeed = MathF.Max(mem.PeakSpeed, 0.5f),
                DistanceToPlayer = Vector2.Distance(npc.Center, player.Center),
                DistanceToCompanion = Vector2.Distance(npc.Center, companion.Center),
            };
            rec.HasSightOnPlayer = LineOfSight.Between(npc, player);
            rec.TicksToPlayer = rec.DistanceToPlayer / rec.ObservedSpeed;
            rec.PredictionConfidence = PredictObservedMotion.Confidence(npc, (int)MathF.Min(180f, rec.TicksToPlayer));
            rec.PredictionSamples = PredictObservedMotion.ErrorSamples(npc);
            rec.EffectiveTicksToPlayer = rec.TicksToPlayer * rec.PredictionConfidence;
            rec.Urgency = !player.dead && rec.CanReachPlayer ? Urgency(rec, player) : 0f;
            // The same reckoning about the companion. Sight is only raycast for threats close
            // enough for the answer to change anything, because this runs per threat per tick and
            // a line-of-sight test is the dearest thing in this loop.
            rec.TicksToCompanion = rec.DistanceToCompanion / rec.ObservedSpeed;
            rec.UrgencyToCompanion = rec.CanReachCompanion ? UrgencyToCompanion(rec, npc, companion) : 0f;
            Threats.Add(rec);

            if (rec.Urgency > (MostUrgent?.Urgency ?? 0f))
                MostUrgent = rec;
            if (rec.UrgencyToCompanion > (MostUrgentToCompanion?.UrgencyToCompanion ?? 0f))
                MostUrgentToCompanion = rec;
            playerMiss *= 1f - MathHelper.Clamp(rec.Urgency, 0f, 1f);
            companionMiss *= 1f - MathHelper.Clamp(rec.UrgencyToCompanion, 0f, 1f);
        }

        PlayerDanger = 1f - playerMiss;
        CompanionDanger = 1f - companionMiss;
    }

    public void SetInterventionEstimate(float ticks)
    {
        InterventionTicks = float.IsFinite(ticks) ? MathF.Max(0f, ticks) : float.PositiveInfinity;
        ProtectionUrgency = 0f;
        Horizon = float.MaxValue;
        foreach (ThreatRecord threat in Threats)
        {
            if (threat.Urgency <= 0f) continue;
            // The weapon estimate names the most urgent target, not every enemy in a crowd.
            // Other threats have no demonstrated intervention yet and cannot borrow that shot.
            float intervention = threat == MostUrgent ? InterventionTicks : float.PositiveInfinity;
            float arrival = threat.Shoots && threat.HasSightOnPlayer ? 0f : threat.EffectiveTicksToPlayer;
            Horizon = MathF.Min(Horizon, MathF.Max(0f, arrival - intervention));
            // Lower confidence shortens the effective arrival estimate above, preserving safety
            // when generic observed continuation has already missed rather than inventing AI.
            float urgency = float.IsPositiveInfinity(intervention) ? 1f
                : MathHelper.Clamp((intervention - arrival + Weights.ProtectionLeadTicks) / Weights.ProtectionLeadTicks, 0f, 1f);
            ProtectionUrgency = MathF.Max(ProtectionUrgency, threat.Urgency * urgency);
        }
    }

    /// <summary>
    /// How much this threat endangers the companion, on the same scale and by the same reasoning
    /// as <see cref="Urgency"/> uses about the player: how hard it hits relative to a life bar,
    /// how soon it arrives, and whether it can see what it is coming for.
    /// </summary>
    private static float UrgencyToCompanion(ThreatRecord t, NPC npc, NPC companion)
    {
        float damageShare = MathHelper.Clamp(t.ExpectedDamage / MathF.Max(1f, companion.lifeMax * 0.25f), 0.2f, 1f);
        float weight = t.IsBoss ? 1f : damageShare;
        float closeness = MathHelper.Clamp(1f - t.TicksToCompanion / 360f, 0f, 1f);
        if (closeness <= 0f)
            return 0f;
        bool sees = t.DistanceToCompanion < SightTestRange && LineOfSight.Between(npc, companion);
        if (t.Shoots && sees)
            closeness = MathF.Max(closeness, 0.8f);
        return weight * closeness * (sees ? 1f : 0.5f);
    }

    /// <summary>Past this the sight test cannot change a decision, so it is not paid for.</summary>
    private const float SightTestRange = 700f;

    /// <summary>
    /// weight(damage, boss) × closeness(time-to-player) × sight factor. Closeness is 1 at
    /// contact and fades to 0 around six seconds out; a shooter with a sight line is
    /// treated as already there.
    /// </summary>
    private static float Urgency(ThreatRecord t, Player player)
    {
        float damageShare = MathHelper.Clamp(t.ExpectedDamage / MathF.Max(1f, player.statLifeMax2 * 0.25f), 0.2f, 1f);
        float weight = t.IsBoss ? 1f : damageShare;
        float closeness = MathHelper.Clamp(1f - t.TicksToPlayer / 360f, 0f, 1f);
        if (t.Shoots && t.HasSightOnPlayer)
            closeness = MathF.Max(closeness, 0.8f);
        float sight = t.HasSightOnPlayer ? 1f : 0.5f;
        return weight * closeness * sight;
    }

}
