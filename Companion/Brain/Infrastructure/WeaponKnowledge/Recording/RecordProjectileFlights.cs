#nullable enable

using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.DataStructures;
using Terraria.ModLoader;

namespace AICompanion.Companion.Brain.Infrastructure.WeaponKnowledge.Recording;

/// <summary>Whose use a watched projectile was spawned by. Both are owned by the local player; only the source tells them apart.</summary>
public enum Shooter { Player, Companion }

/// <summary>Why a watched projectile died, read from what its trace holds at death.</summary>
public enum DeathCause { Alive, Expired, PierceSpent, Wall, Other }

/// <summary>What coincided with a child spawn in the same update of its parent.</summary>
public enum ChildTrigger { OnBodyHit, OnWallContact, OnParentDeath, Timer }

/// <summary>
/// One velocity sample of a watched projectile. Post-AI samples arrive per sub-step, before movement;
/// settled samples arrive once per tick from <c>PostUpdateProjectiles</c>, after the move. The arc learner
/// reads the post-AI series; the flight-law fitter reads both and tells them apart by <see cref="Settled"/>.
/// A zero <see cref="ToNearestEligibleNpc"/> means no eligible body existed, not a body at the centre.
/// </summary>
public readonly record struct FlightStep(int Update, Vector2 Position, Vector2 Velocity,
    Vector2 ToNearestEligibleNpc, Vector2 ToAimPoint, Vector2 ToOwner, bool Wet, bool Settled);

/// <summary>One tile contact: the velocity in at the collide hook, the velocity out filled from the settled tick after it.</summary>
public readonly record struct WallContact(int Update, Vector2 Position, Vector2 VelocityIn, Vector2 VelocityOut, Vector2 Normal, bool Died);

/// <summary>One body struck: whether its box overlapped the projectile's own tells a contact hit from area damage.</summary>
public readonly record struct BodyHit(int Update, int NpcType, Vector2 ProjectileCentre, Rectangle NpcBox, bool BoxOverlapsProjectile, int Damage, int HitIndex);

/// <summary>One child spawn, in the parent's velocity frame so offsets rotate with the shot.</summary>
public readonly record struct ChildSpawn(int Update, int ChildType, Vector2 OffsetInParentFrame, Vector2 VelocityInParentFrame, ChildTrigger Trigger, float DamageRatio);

/// <summary>
/// Everything watched about one projectile's flight, from spawn to death. Use samples (the player's and the
/// companion's own shots) feed the arc learner until phase C's fitter replaces it; children are traced for the
/// child-spawn learner and never teach arcs, because a child that retargets would bend its type's gravity.
/// </summary>
public sealed class FlightTrace
{
    public int ProjectileType;
    public Shooter Shooter;
    public Simulation.ModifierState Modifiers;
    public int DeclaredPenetrate;
    public int DeclaredLifetime;
    public Vector2 AimPoint;
    public bool AimKnown;
    public bool UseSample;
    public int Update;
    public int PendingWall = -1;
    public List<FlightStep> Steps = new();
    public List<WallContact> Walls = new();
    public List<BodyHit> Hits = new();
    public List<ChildSpawn> Children = new();
    public DeathCause Death = DeathCause.Alive;
}

/// <summary>
/// Every friendly damaging flight, watched to its death: the player's uses as well as the companion's, because the
/// player fires most weapons many times before handing one over, and every child of a watched flight for the spawn
/// learner. The engine calls the <c>Note*</c> methods through the hook classes below; a headless fixture calls them
/// directly around <c>Projectile.VanillaAI</c>, which is the whole of what headless can run. Use samples are also fed
/// to the arc learner, which is what the companion aims with until phase C; that feed moved here from the firing
/// hooks in phase B, and the landed-hit ledger and outcome windows kept theirs.
/// </summary>
public static class RecordProjectileFlights
{
    /// <summary>Closed traces kept per projectile type: the eight with the most steps, because a longer flight carries more evidence about the dynamics than a short one.</summary>
    public const int MaxTracesPerType = 8;

    private static readonly Dictionary<int, FlightTrace> open = new();
    private static readonly Dictionary<int, List<FlightTrace>> closed = new();

    public static FlightTrace? TraceOf(int projectileSlot)
        => open.TryGetValue(projectileSlot, out FlightTrace? trace) ? trace : null;

    /// <summary>Every live companion trace, so the overlay can draw flown paths beside the simulated ones.</summary>
    public static IEnumerable<FlightTrace> OpenCompanionTraces()
    {
        foreach (FlightTrace trace in open.Values)
            if (trace.Shooter == Shooter.Companion)
                yield return trace;
    }

    public static IReadOnlyList<FlightTrace> ClosedFor(int projectileType)
        => closed.TryGetValue(projectileType, out List<FlightTrace>? traces) ? traces : Array.Empty<FlightTrace>();

    /// <summary>
    /// The aim point a registered companion shot was fired at, for the cursor spoof. Empty when the slot holds no
    /// trace, its aim was never registered, or the trace is the player's: the player's own shots fly under his own
    /// cursor, and spoofing them would steer his game, not the companion's. A child inherits its parent's shooter,
    /// so a player's splitting shot never arms the spoof through its children.
    /// </summary>
    public static Vector2? AimFor(int projectileSlot)
        => open.TryGetValue(projectileSlot, out FlightTrace? trace) && trace.Shooter == Shooter.Companion && trace.AimKnown
            ? trace.AimPoint : null;

    /// <summary>
    /// A native spawn. A use by the local player opens a player trace from its source; a child of a watched
    /// projectile opens a child trace and is entered on its parent. Companion shots are opened explicitly by
    /// <see cref="NoteCompanionSpawn"/>, because their source names no item and no aim — the hook cannot tell
    /// them from any other friendly spawn, and guessing from the NPC would couple knowledge to the body.
    /// Every spawn first closes whatever trace the slot still holds, so a reused slot never inherits a flight.
    /// </summary>
    public static void NoteSpawn(int projectileSlot, Projectile projectile, IEntitySource? source)
    {
        CloseOpen(projectileSlot);
        if (source is EntitySource_Parent { Entity: Projectile parent } && open.TryGetValue(parent.whoAmI, out FlightTrace? parentTrace) && parent.whoAmI != projectileSlot)
        {
            FlightTrace child = Open(projectileSlot, projectile, parentTrace.Shooter, useSample: false);
            child.AimPoint = parentTrace.AimPoint;
            child.AimKnown = parentTrace.AimKnown;
            parentTrace.Children.Add(new ChildSpawn(parentTrace.Update, projectile.type,
                IntoFrame(projectile.Center - parent.Center, parent.velocity),
                IntoFrame(projectile.velocity, parent.velocity),
                TriggerFor(parentTrace),
                parent.damage > 0 ? projectile.damage / (float)parent.damage : 1f));
            return;
        }
        if (source is EntitySource_ItemUse use && use.Player.whoAmI == Main.myPlayer && use.Item != null)
        {
            FlightTrace player = Open(projectileSlot, projectile, Shooter.Player, useSample: true);
            player.AimPoint = Main.MouseWorld;
            player.AimKnown = true;
            GroupSpawnsIntoUses.NoteSpawn((int)Main.GameUpdateCount, projectileSlot, projectile, use.Item.type,
                (source as EntitySource_ItemUse_WithAmmo)?.AmmoItemIdUsed ?? 0, player.AimPoint);
        }
    }

    /// <summary>
    /// A shot the companion's own hand just spawned: open its trace and join its use, which <c>ItemWeapon.Fire</c>
    /// opened first. The arc learner watches it from the launch on, exactly as it watched single shots before volleys.
    /// The trace records the modifier state the hand applied, so the learners that could absorb it exclude it.
    /// </summary>
    public static void NoteCompanionSpawn(int projectileSlot, Projectile projectile, int useId, Simulation.ModifierState modifiers)
    {
        CloseOpen(projectileSlot);
        FlightTrace trace = Open(projectileSlot, projectile, Shooter.Companion, useSample: true);
        trace.Modifiers = modifiers;
        GroupSpawnsIntoUses.JoinCompanionSpawn(useId, (int)Main.GameUpdateCount, projectileSlot, projectile);
    }

    /// <summary>The aim point a companion shot was fired at: the target's centre on the tick it left. The trace opens aimless at spawn and learns it here.</summary>
    public static void NoteCompanionAim(int projectileSlot, Vector2 aimPoint)
    {
        if (open.TryGetValue(projectileSlot, out FlightTrace? trace) && trace.Shooter == Shooter.Companion)
        {
            trace.AimPoint = aimPoint;
            trace.AimKnown = true;
        }
    }

    /// <summary>One post-AI velocity, per sub-step, before movement.</summary>
    public static void NoteStep(Projectile projectile)
    {
        if (!open.TryGetValue(projectile.whoAmI, out FlightTrace? trace)) return;
        if (!projectile.active || projectile.type != trace.ProjectileType)
        {
            Close(projectile.whoAmI, trace, DeathCause.Other);
            return;
        }
        trace.Steps.Add(Sample(trace, projectile, settled: false));
    }

    /// <summary>One settled tick, after movement: the position the sub-steps never see, and the velocity out of any wall contact since the last one.</summary>
    public static void NoteSettled(Projectile projectile)
    {
        if (!open.TryGetValue(projectile.whoAmI, out FlightTrace? trace)) return;
        trace.Steps.Add(Sample(trace, projectile, settled: true));
        if (trace.PendingWall >= 0 && trace.PendingWall < trace.Walls.Count)
        {
            WallContact contact = trace.Walls[trace.PendingWall];
            bool died = !projectile.active;
            trace.Walls[trace.PendingWall] = contact with
            {
                VelocityOut = projectile.velocity,
                Normal = ContactNormal(contact.VelocityIn, projectile.velocity),
                Died = died,
            };
            trace.PendingWall = -1;
        }
        if (!projectile.active)
            Close(projectile.whoAmI, trace, DeathCause.Other);
    }

    /// <summary>A tile contact, with the pre-collision velocity. The velocity out is filled from the settled tick after it; a death before then fills it from the death.</summary>
    public static void NoteWallContact(Projectile projectile, Vector2 oldVelocity)
    {
        if (!open.TryGetValue(projectile.whoAmI, out FlightTrace? trace)) return;
        trace.PendingWall = trace.Walls.Count;
        trace.Walls.Add(new WallContact(trace.Update, projectile.Center, oldVelocity, Vector2.Zero, Vector2.Zero, Died: false));
    }

    /// <summary>A body struck, from the NPC on-hit hook: the hit's ordinal on this flight, and whether the boxes overlapped or the damage is area.</summary>
    public static void NoteHit(int projectileSlot, NPC npc, Projectile projectile, int damageDone)
    {
        if (!open.TryGetValue(projectileSlot, out FlightTrace? trace)) return;
        trace.Hits.Add(new BodyHit(trace.Update, npc.type, projectile.Center, npc.Hitbox,
            projectile.Hitbox.Intersects(npc.Hitbox), damageDone, trace.Hits.Count));
    }

    /// <summary>The watched projectile died: decide why from what the trace holds, retire its arc watch, and keep the closed trace.</summary>
    public static void NoteDeath(Projectile projectile)
    {
        if (!open.TryGetValue(projectile.whoAmI, out FlightTrace? trace)) return;
        // A child spawned while the parent dies files before the death lands — the spawn hook runs inside the
        // kill ahead of this one — so it reads Timer at spawn time. Anything in the last two updates that found
        // no other trigger is the death's, reclassified now that the death is known.
        for (int i = 0; i < trace.Children.Count; i++)
        {
            ChildSpawn child = trace.Children[i];
            if (child.Trigger == ChildTrigger.Timer && child.Update >= trace.Update - 2)
                trace.Children[i] = child with { Trigger = ChildTrigger.OnParentDeath };
        }
        if (trace.PendingWall >= 0 && trace.PendingWall < trace.Walls.Count)
        {
            WallContact contact = trace.Walls[trace.PendingWall];
            trace.Walls[trace.PendingWall] = contact with
            {
                VelocityOut = projectile.velocity,
                Normal = ContactNormal(contact.VelocityIn, projectile.velocity),
                Died = true,
            };
            trace.PendingWall = -1;
        }
        DeathCause death = DeathCause.Other;
        if (trace.Walls.Exists(w => w.Died))
            death = DeathCause.Wall;
        else if (trace.DeclaredPenetrate > 0 && trace.Hits.Count >= trace.DeclaredPenetrate)
            death = DeathCause.PierceSpent;
        else if (trace.Update >= trace.DeclaredLifetime)
            death = DeathCause.Expired;
        if (trace.UseSample && trace.Shooter == Shooter.Companion)
            Diagnostics.GodsEyeEvents.RecordShotEvent(projectile.whoAmI, trace.ProjectileType, death.ToString().ToLowerInvariant(),
                trace.Walls.Count, FirstWall(trace), trace.Hits.Count, FirstHit(trace), trace.Children.Count, ChildTypes(trace));
        Close(projectile.whoAmI, trace, death);
    }

    private static string FirstWall(FlightTrace trace)
    {
        if (trace.Walls.Count == 0) return "-";
        WallContact wall = trace.Walls[0];
        return FormattableString.Invariant($"tick={wall.Update};in={wall.VelocityIn.X:0.0},{wall.VelocityIn.Y:0.0};out={wall.VelocityOut.X:0.0},{wall.VelocityOut.Y:0.0}");
    }

    private static string FirstHit(FlightTrace trace)
    {
        if (trace.Hits.Count == 0) return "-";
        BodyHit hit = trace.Hits[0];
        return FormattableString.Invariant($"type={hit.NpcType};tick={hit.Update};damage={hit.Damage}");
    }

    private static string ChildTypes(FlightTrace trace)
    {
        if (trace.Children.Count == 0) return "-";
        var types = new List<int>(trace.Children.Count);
        foreach (ChildSpawn child in trace.Children)
            types.Add(child.ChildType);
        types.Sort();
        return string.Join(",", types);
    }

    public static void Clear()
    {
        open.Clear();
        closed.Clear();
    }

    private static FlightTrace Open(int projectileSlot, Projectile projectile, Shooter shooter, bool useSample)
    {
        var trace = new FlightTrace
        {
            ProjectileType = projectile.type,
            Shooter = shooter,
            DeclaredPenetrate = projectile.penetrate,
            DeclaredLifetime = projectile.timeLeft,
            UseSample = useSample,
        };
        open[projectileSlot] = trace;
        return trace;
    }

    private static void CloseOpen(int projectileSlot)
    {
        if (open.TryGetValue(projectileSlot, out FlightTrace? trace))
            Close(projectileSlot, trace, DeathCause.Other);
    }

    private static void Close(int projectileSlot, FlightTrace trace, DeathCause death)
    {
        open.Remove(projectileSlot);
        trace.Death = death;
        if (!closed.TryGetValue(trace.ProjectileType, out List<FlightTrace>? traces))
            closed[trace.ProjectileType] = traces = new List<FlightTrace>();
        traces.Add(trace);
        // The kept set is the most informative, not the newest: a new trace replaces the one with the fewest
        // steps, because evidence is what reduces the fitter's uncertainty and a short flight carries least.
        // Ties drop the oldest, so equal-length flights still forget in order and the set adapts to change.
        while (traces.Count > MaxTracesPerType)
        {
            int drop = 0;
            for (int i = 1; i < traces.Count; i++)
                if (traces[i].Steps.Count < traces[drop].Steps.Count)
                    drop = i;
            traces.RemoveAt(drop);
        }
        Learning.LearnWallResponses.Learn(trace);
        Learning.LearnHitResponses.Learn(trace);
        Learning.LearnChildSpawns.Learn(trace);
        Learning.FitFlightLaws.Notice(trace);
    }

    private static FlightStep Sample(FlightTrace trace, Projectile projectile, bool settled)
    {
        Vector2 toAim = trace.AimKnown ? trace.AimPoint - projectile.Center : Vector2.Zero;
        return new FlightStep(trace.Update++, projectile.Center, projectile.velocity,
            ToNearestEligibleNpc(projectile), toAim, Main.LocalPlayer.Center - projectile.Center, projectile.wet, settled);
    }

    private static Vector2 ToNearestEligibleNpc(Projectile projectile)
    {
        float best = float.MaxValue;
        Vector2 offset = Vector2.Zero;
        foreach (NPC npc in Main.npc)
        {
            if (npc == null || !npc.active || !npc.CanBeChasedBy()) continue;
            float distance = Vector2.DistanceSquared(npc.Center, projectile.Center);
            if (distance < best)
            {
                best = distance;
                offset = npc.Center - projectile.Center;
            }
        }
        return offset;
    }

    private static ChildTrigger TriggerFor(FlightTrace parent)
    {
        foreach (BodyHit hit in parent.Hits)
            if (hit.Update == parent.Update) return ChildTrigger.OnBodyHit;
        foreach (WallContact wall in parent.Walls)
            if (wall.Update == parent.Update) return ChildTrigger.OnWallContact;
        return parent.Death != DeathCause.Alive ? ChildTrigger.OnParentDeath : ChildTrigger.Timer;
    }

    private static Vector2 IntoFrame(Vector2 vector, Vector2 axis)
    {
        if (axis == Vector2.Zero) return vector;
        float angle = MathF.Atan2(axis.Y, axis.X);
        float cos = MathF.Cos(-angle), sin = MathF.Sin(-angle);
        return new Vector2(vector.X * cos - vector.Y * sin, vector.X * sin + vector.Y * cos);
    }

    private static Vector2 ContactNormal(Vector2 velocityIn, Vector2 velocityOut)
    {
        bool flipX = MathF.Sign(velocityOut.X) != MathF.Sign(velocityIn.X) && velocityIn.X != 0f;
        bool flipY = MathF.Sign(velocityOut.Y) != MathF.Sign(velocityIn.Y) && velocityIn.Y != 0f;
        if (!flipX && !flipY) return Vector2.Zero;
        Vector2 normal = new(flipX ? 1f : 0f, flipY ? 1f : 0f);
        return normal == Vector2.Zero ? Vector2.Zero : Vector2.Normalize(normal);
    }
}

/// <summary>
/// The engine side of the flight recorder: spawns open traces, post-AI velocities step them, tile contacts
/// and deaths close the loop. The landed-hit ledger and outcome windows kept their own hook in the firing
/// interaction; the arc-learner feed that used to live there moved into these calls in phase B.
/// </summary>
public sealed class WatchProjectileFlights : GlobalProjectile
{
    public override void OnSpawn(Projectile projectile, IEntitySource source)
        => RecordProjectileFlights.NoteSpawn(projectile.whoAmI, projectile, source);

    public override void PostAI(Projectile projectile)
        => RecordProjectileFlights.NoteStep(projectile);

    public override bool OnTileCollide(Projectile projectile, Vector2 oldVelocity)
    {
        RecordProjectileFlights.NoteWallContact(projectile, oldVelocity);
        return true;
    }

    public override void OnKill(Projectile projectile, int timeLeft)
        => RecordProjectileFlights.NoteDeath(projectile);
}

/// <summary>
/// Once per tick, after everything moved: settled positions for every open trace, and the player's animation
/// for the use grouper. Both read settled state, so both live after their updates.
/// </summary>
public sealed class SettleProjectileFlights : ModSystem
{
    public override void PostUpdateProjectiles()
    {
        foreach (Projectile projectile in Main.projectile)
        {
            if (projectile == null || projectile.whoAmI < 0) continue;
            if (RecordProjectileFlights.TraceOf(projectile.whoAmI) != null)
                RecordProjectileFlights.NoteSettled(projectile);
        }
    }

    public override void PostUpdatePlayers()
    {
        Player player = Main.LocalPlayer;
        GroupSpawnsIntoUses.NotePlayerAnimation((int)Main.GameUpdateCount, player.itemAnimation, player.itemAnimationMax,
            player.HeldItem?.type ?? 0, ActiveBuffs(player));
    }

    private static int[] ActiveBuffs(Player player)
    {
        var buffs = new List<int>();
        for (int i = 0; i < player.buffType.Length; i++)
            if (player.buffTime[i] > 0)
                buffs.Add(player.buffType[i]);
        buffs.Sort();
        return buffs.ToArray();
    }
}
