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
        /// <summary>One-shot: the audit stamped post-update values and the next update builds its
        /// records from them without decaying, invalidating or refreshing. Consumed by that update.</summary>
        public bool Trusted;
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

    /// <summary>
    /// One slot's post-update memory in plain values, for the combat snapshot: the peak speed and the two
    /// reachability answers are the whole of what the record half reads from memory, so they are the whole
    /// of what the audit restores. The type, generation, positions, world and refresh tick live only in the
    /// skipped half — the reset, invalidation and refresh the trusted update does not run — and are not carried.
    /// </summary>
    public bool TryExportMemory(int slot, out float peakSpeed, out bool canReachPlayer, out bool canReachCompanion)
    {
        Memory? mem = (uint)slot < (uint)memory.Length ? memory[slot] : null;
        peakSpeed = mem?.PeakSpeed ?? 1f;
        canReachPlayer = mem?.CanReachPlayer ?? true;
        canReachCompanion = mem?.CanReachCompanion ?? true;
        return mem != null;
    }

    /// <summary>
    /// Install one slot's memory read back from a snapshot. The next update consumes the trust: it builds
    /// its records from these values without decaying, invalidating or refreshing, so its records are the
    /// live tick's records. One-shot, because a refresh the live tick ran stays run.
    /// </summary>
    public void AssumeThreatMemory(int slot, float peakSpeed, bool canReachPlayer, bool canReachCompanion)
    {
        if ((uint)slot >= (uint)memory.Length)
            return;
        Memory mem = memory[slot] ??= new Memory();
        mem.PeakSpeed = peakSpeed;
        mem.CanReachPlayer = canReachPlayer;
        mem.CanReachCompanion = canReachCompanion;
        mem.Trusted = true;
    }

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

        // The cell each body occupies, which is what a walking enemy's reach flood is asked about: the
        // player stands, so it is the row above his feet; the orb hovers, so it is the cell its centre is in.
        Point playerFeet = MovementQueries.FeetTile(player.Bottom);
        Point companionFeet = MovementQueries.Tile(companion.Center);

        foreach (NPC npc in Main.ActiveNPCs)
        {
            int projectileDamage = HostileAttackSources.RecentDamage(npc);
            if (npc.friendly || npc.life <= 0 || (npc.damage <= 0 && projectileDamage <= 0) || npc.CountsAsACritter)
                continue;

            PredictObservedMotion.Observe(npc);
            Memory mem = memory[npc.whoAmI] ??= new Memory();
            // A trusted slot carries the live tick's post-update memory, stamped by the audit: the records
            // below read its peak speed and reachability as they stand, and none of the decay, invalidation
            // or refresh that produced them runs again. The motion observe above still runs — its early
            // return is the tripwire that the restored body stands where the snapshot said it did.
            bool trusted = mem.Trusted;
            mem.Trusted = false;
            if (!trusted && (mem.Type != npc.type || mem.Generation != HostileAttackSources.Generation(npc)))
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
            if (!trusted)
                mem.PeakSpeed = MathF.Max(mem.PeakSpeed * SpeedDecay, speed);

            Point from = cls == MovementClass.Walker ? MovementQueries.FeetTile(npc.Bottom) : npc.Center.ToTileCoordinates();
            Point playerTarget = cls == MovementClass.Walker ? playerFeet : player.Center.ToTileCoordinates();
            // A walker is asked about the floor under the orb, not the cell the orb hovers in. The cell changes with
            // every few pixels of drift, and a changed target reads reachable until the next budgeted refresh, so a
            // drifting body read as reachable by every walker on almost every tick; the floor under it holds still.
            Point companionTarget = cls == MovementClass.Walker
                ? EstimateEnemyReach.Landing(MovementQueries.World, companionFeet) : companion.Center.ToTileCoordinates();
            if (!trusted)
            {
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
            }

            var rec = new ThreatRecord
            {
                Npc = npc,
                Class = cls,
                CanReachPlayer = cls == MovementClass.Phaser || mem.CanReachPlayer,
                CanReachCompanion = cls == MovementClass.Phaser || (mem.CanReachCompanion && WithinJump(cls, npc, companion, companionTarget)),
                Shoots = projectileDamage > 0,
                ExpectedDamage = Math.Max(npc.damage, projectileDamage),
                IsBoss = npc.boss,
                ObservedSpeed = MathF.Max(mem.PeakSpeed, 0.5f),
                DistanceToPlayer = Vector2.Distance(npc.Center, player.Center),
                DistanceToCompanion = Vector2.Distance(npc.Center, companion.Center),
            };
            rec.EffectiveDamageToPlayer = EstimateEffectiveDamage.ToPlayer(player, rec.ExpectedDamage);
            rec.EffectiveDamageToCompanion = EstimateEffectiveDamage.ToNpc(companion, rec.ExpectedDamage);
            rec.HasSightOnPlayer = LineOfSight.Between(npc, player);
            rec.TicksToPlayer = rec.DistanceToPlayer / rec.ObservedSpeed;
            rec.PredictionConfidence = PredictObservedMotion.Confidence(npc, (int)MathF.Min(180f, rec.TicksToPlayer));
            rec.PredictionSamples = PredictObservedMotion.ErrorSamples(npc);
            rec.EffectiveTicksToPlayer = rec.TicksToPlayer * rec.PredictionConfidence;
            rec.Urgency = !player.dead && rec.CanReachPlayer
                ? ThreatUrgency.ToPlayer(rec.EffectiveDamageToPlayer, player.statLife, rec.IsBoss, rec.TicksToPlayer, rec.Shoots, rec.HasSightOnPlayer)
                : 0f;
            // The same reckoning about the companion. Sight is only raycast for threats that can reach it and are close
            // enough for the answer to change anything, because this runs per threat per tick and a line-of-sight test is
            // the dearest thing in this loop. The answer is kept on the record so a consumer re-weighing this threat at
            // another distance uses the sight this tick decided on rather than paying for its own.
            rec.TicksToCompanion = rec.DistanceToCompanion / rec.ObservedSpeed;
            rec.HasSightOnCompanion = rec.CanReachCompanion && ThreatUrgency.Closeness(rec.TicksToCompanion) > 0f
                && rec.DistanceToCompanion < SightTestRange && LineOfSight.Between(npc, companion);
            rec.UrgencyToCompanion = rec.CanReachCompanion
                ? ThreatUrgency.ToCompanion(rec.EffectiveDamageToCompanion, companion.life, rec.IsBoss, rec.TicksToCompanion, rec.Shoots, rec.HasSightOnCompanion)
                : 0f;
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
    /// How high above its floor a walking enemy can reach: the highest jump of the game's fighter AI, under the NPC
    /// default gravity of 0.3 (<c>vanillaGravity = 0.3f</c>), which peaks v²/2g above where it left the floor.
    /// <c>NPC.AI_003_Fighters</c> jumps at -8 for an ordinary step and steps up through -8.8, -10, -10.3 and -10.6
    /// to -11 for the tallest obstacle it will climb, so the highest is taken: the first envelope used -8, a 107 px
    /// apex, and a walker at the foot of a tall step could then reach an orb the sense had called safe. Read off the
    /// decompiled game rather than tuned, and it errs toward reading danger. Still under-read, and named rather than
    /// guessed at: a walker in water (gravity 0.2) or in the low gravity near space, and a modded walker that jumps
    /// higher than the fighter AI.
    /// </summary>
    private const float WalkerJumpApexPixels = 11f * 11f / (2f * 0.3f);

    /// <summary>
    /// Whether a walking enemy on the floor under the orb could touch it at all: the orb's circle has to come
    /// within the walker's own height plus its highest jump above that floor. A hovering orb out of that reach
    /// cannot be hit by a walker however near it stands, which is what the danger reading got wrong in the
    /// first play of the orb, reading 0.92 to 0.97 with zombies below while the orb took no hit in 83 seconds.
    /// Everything that is not a walker is not asked. The jump is measured from the floor under the orb or from where the
    /// walker already stands, whichever is higher, because a walker on a ledge level with the orb, or already in the air
    /// beside it, does not have to come down to the orb's floor before it can touch it.
    /// </summary>
    private static bool WithinJump(MovementClass cls, NPC npc, NPC companion, Point floorCell)
    {
        if (cls != MovementClass.Walker) return true;
        float floorSurface = (floorCell.Y + 1) * 16f;
        float highestReach = MathF.Min(floorSurface - npc.height, npc.position.Y) - WalkerJumpApexPixels;
        return companion.Center.Y + CircleContact.Radius >= highestReach;
    }

    /// <summary>Past this the sight test cannot change a decision, so it is not paid for.</summary>
    private const float SightTestRange = 700f;
}
