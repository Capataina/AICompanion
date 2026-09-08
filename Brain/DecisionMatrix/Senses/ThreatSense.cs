#nullable enable

using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using AICompanion.Brain.DecisionMatrix.Navigation;

namespace AICompanion.Brain.DecisionMatrix.Senses;

/// <summary>
/// Every hostile that matters, and the two numbers the brain leans on: the player's
/// danger and the safety horizon. A threat that cannot reach the player contributes
/// nothing. Reachability and speed history are cached per NPC slot and refreshed on a
/// stagger so the expensive searches never all run in one tick.
/// </summary>
public sealed class ThreatSense
{
    private const int ReachabilityRefreshTicks = 60;
    private const float SpeedDecay = 0.995f;

    /// <summary>
    /// Hostiles whose AI spawns a projectile, pre-hardmode. Demon Eyes are not here: their
    /// AI (AI_002_FloatingEye) never calls NewProjectile. Modded shooters are learned by
    /// watching projectiles appear next to them.
    /// </summary>
    private static readonly HashSet<int> KnownShooters = new()
    {
        NPCID.GoblinArcher, NPCID.Hornet, NPCID.Harpy, NPCID.DarkCaster, NPCID.Antlion,
        NPCID.SkeletonArcher, NPCID.FireImp, NPCID.SnowFlinx,
        NPCID.SpikedJungleSlime, NPCID.SpikedIceSlime, NPCID.GoblinSorcerer,
    };

    /// <summary>Projectiles already attributed to a shooter, so each is counted once, at birth.</summary>
    private readonly HashSet<int> seenProjectiles = new();
    private readonly int[] projectileIdentity = new int[Main.maxProjectiles];

    private sealed class Memory
    {
        public int Type;
        public float PeakSpeed;
        public bool Reachable = true;
        public int ReachableCheckedAt = -1000;
        public int LastShotSeenAt = -1000;
    }

    private readonly Memory[] memory = new Memory[Main.maxNPCs];
    private int tick;

    public readonly List<ThreatRecord> Threats = new();

    /// <summary>0..1, the most urgent reachable threat to the player.</summary>
    public float PlayerDanger { get; private set; }

    /// <summary>Ticks the companion may stay away before the player is at risk; float.MaxValue when nothing threatens.</summary>
    public float Horizon { get; private set; } = float.MaxValue;

    public bool PlayerIsSafe => PlayerDanger < 0.25f;
    public ThreatRecord? MostUrgent { get; private set; }

    public void Update(Player player, NPC companion)
    {
        tick++;
        Threats.Clear();
        PlayerDanger = 0f;
        Horizon = float.MaxValue;
        MostUrgent = null;

        LearnShootersFromProjectiles();

        Point playerFeet = NavGrid.FeetTile(player.Bottom);
        float companionReturnTicks = Vector2.Distance(companion.Bottom, player.Bottom) / Companion.CompanionMotor.WalkSpeed;

        foreach (NPC npc in Main.ActiveNPCs)
        {
            if (npc.friendly || npc.life <= 0 || npc.damage <= 0 || npc.CountsAsACritter || !npc.CanBeChasedBy())
                continue;

            Memory mem = memory[npc.whoAmI] ??= new Memory();
            if (mem.Type != npc.type)
            {
                mem.Type = npc.type;
                mem.PeakSpeed = 1f;
                mem.ReachableCheckedAt = -1000;
            }

            MovementClass cls = npc.noTileCollide ? MovementClass.Phaser : (npc.noGravity ? MovementClass.Flyer : MovementClass.Walker);

            // A walker's approach speed is horizontal; its fall speed after a hop is not how fast it closes.
            float speed = cls == MovementClass.Walker ? MathF.Abs(npc.velocity.X) : npc.velocity.Length();
            mem.PeakSpeed = MathF.Max(mem.PeakSpeed * SpeedDecay, speed);

            if (cls != MovementClass.Phaser && tick - mem.ReachableCheckedAt >= ReachabilityRefreshTicks && (tick + npc.whoAmI) % 4 == 0)
            {
                Point from = NavGrid.FeetTile(npc.Bottom);
                mem.Reachable = cls == MovementClass.Walker
                    ? Reachability.WalkerCanReach(from, playerFeet)
                    : Reachability.FlyerCanReach(new Point((int)(npc.Center.X / 16f), (int)(npc.Center.Y / 16f)), new Point((int)(player.Center.X / 16f), (int)(player.Center.Y / 16f)));
                mem.ReachableCheckedAt = tick;
            }

            var rec = new ThreatRecord
            {
                Npc = npc,
                Class = cls,
                Reachable = cls == MovementClass.Phaser || mem.Reachable,
                Shoots = KnownShooters.Contains(npc.type) || tick - mem.LastShotSeenAt < 240,
                IsBoss = npc.boss,
                ObservedSpeed = MathF.Max(mem.PeakSpeed, 0.5f),
                DistanceToPlayer = Vector2.Distance(npc.Center, player.Center),
                DistanceToCompanion = Vector2.Distance(npc.Center, companion.Center),
            };
            rec.HasSightOnPlayer = LineOfSight.Between(npc, player);
            rec.TicksToPlayer = rec.DistanceToPlayer / rec.ObservedSpeed;
            rec.Urgency = rec.Reachable ? Urgency(rec, player) : 0f;
            Threats.Add(rec);

            if (rec.Urgency > PlayerDanger)
            {
                PlayerDanger = rec.Urgency;
                MostUrgent = rec;
            }
            if (rec.Reachable)
            {
                float arrives = rec.Shoots && rec.HasSightOnPlayer ? 0f : rec.TicksToPlayer;
                Horizon = MathF.Min(Horizon, MathF.Max(0f, arrives - companionReturnTicks));
            }
        }
    }

    /// <summary>
    /// weight(damage, boss) × closeness(time-to-player) × sight factor. Closeness is 1 at
    /// contact and fades to 0 around six seconds out; a shooter with a sight line is
    /// treated as already there.
    /// </summary>
    private static float Urgency(ThreatRecord t, Player player)
    {
        float damageShare = MathHelper.Clamp(t.Npc.damage / MathF.Max(1f, player.statLifeMax2 * 0.25f), 0.2f, 1f);
        float weight = t.IsBoss ? 1f : damageShare;
        float closeness = MathHelper.Clamp(1f - t.TicksToPlayer / 360f, 0f, 1f);
        if (t.Shoots && t.HasSightOnPlayer)
            closeness = MathF.Max(closeness, 0.8f);
        float sight = t.HasSightOnPlayer ? 1f : 0.5f;
        return weight * closeness * sight;
    }

    /// <summary>
    /// A hostile projectile, on the tick it first appears, marks the nearest hostile NPC
    /// within 48 px as a shooter for a few seconds. Counting only at birth keeps a
    /// zombie that walks under an arrow's flight from inheriting the label.
    /// </summary>
    private void LearnShootersFromProjectiles()
    {
        foreach (Projectile p in Main.ActiveProjectiles)
        {
            if (!p.hostile || p.friendly)
                continue;
            int identity = p.identity;
            if (projectileIdentity[p.whoAmI] == identity && seenProjectiles.Contains(p.whoAmI))
                continue;
            projectileIdentity[p.whoAmI] = identity;
            seenProjectiles.Add(p.whoAmI);

            NPC? nearest = null;
            float best = 48f * 48f;
            foreach (NPC npc in Main.ActiveNPCs)
            {
                if (npc.friendly)
                    continue;
                float d = Vector2.DistanceSquared(npc.Center, p.Center);
                if (d < best)
                {
                    best = d;
                    nearest = npc;
                }
            }
            if (nearest != null)
                (memory[nearest.whoAmI] ??= new Memory()).LastShotSeenAt = tick;
        }
    }
}
