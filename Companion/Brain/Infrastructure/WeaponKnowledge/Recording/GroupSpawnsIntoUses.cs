#nullable enable

using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;

namespace AICompanion.Companion.Brain.Infrastructure.WeaponKnowledge.Recording;

/// <summary>One projectile as a use saw it spawn: where, how fast, at what damage, and from what ammo.</summary>
public readonly record struct UseSpawn(int Slot, int ProjectileType, int Tick, Vector2 Position, Vector2 Velocity,
    int Damage, int AmmoItemId, Vector2 ShooterCentre, Vector2 AimPoint);

/// <summary>
/// One use of one item: every projectile it spawned, grouped by the animation for the player and by the
/// firing call for the companion. A use the grouping joined mid-animation is partial: its pellets still teach
/// their slots, but its count is short and must not teach how many a use holds.
/// </summary>
public sealed class ProjectileUse
{
    public int Id;
    public Shooter Shooter;
    public int ItemType;
    public int StartTick;
    public int? EndTick;
    public Vector2 AimPoint;
    public Vector2 ShooterCentre;
    public float ComposedDamage;
    public float ComposedSpeed;
    public int[] BuffsAtStart = Array.Empty<int>();
    public bool Complete = true;
    public readonly List<UseSpawn> Spawns = new();
}

/// <summary>
/// Spawn samples grouped into the uses that spawned them. A player use begins on the tick his
/// <c>itemAnimation</c> resets to its max for the item and collects every projectile his item-use source
/// spawns until the animation ends, which captures same-tick spreads, sky rains and bursts across the
/// animation with one rule. A companion use is opened and closed by <c>ItemWeapon.Fire</c> itself, because
/// its source names neither an item nor an aim. A closed use is taught to the volley shapes at once.
/// </summary>
public static class GroupSpawnsIntoUses
{
    /// <summary>Closed uses kept for fixtures and inspection; learning reads each use once, at closing.</summary>
    public const int MaxClosedUsesKept = 32;

    private static int nextId = 1;
    private static ProjectileUse? openPlayer;
    private static ProjectileUse? pendingPlayer;
    private static ProjectileUse? openCompanion;
    private static int previousAnimation = -1;
    private static int previousMax = -1;
    private static readonly List<ProjectileUse> closed = new();

    public static IReadOnlyList<ProjectileUse> ClosedUses => closed;

    /// <summary>
    /// One tick's player animation sample, after players update. An animation at zero parks its use as
    /// pending rather than closing it, because the tick's spawns update after the players and still belong
    /// to it; the pending use closes on the next sample, or when a new use opens. A first sample mid-swing
    /// opens a partial use.
    /// </summary>
    public static void NotePlayerAnimation(int tick, int itemAnimation, int itemAnimationMax, int heldItemType, int[] buffs)
    {
        if (openPlayer != null && itemAnimation == 0)
        {
            FlushPending();
            pendingPlayer = openPlayer;
            openPlayer = null;
        }
        else if (pendingPlayer != null)
        {
            FlushPending();
        }
        if (itemAnimationMax > 0 && itemAnimation == itemAnimationMax && !(previousAnimation == itemAnimationMax && previousMax == itemAnimationMax))
        {
            if (openPlayer != null)
            {
                openPlayer.Complete = false;
                Learn(openPlayer);
                openPlayer = null;
            }
            openPlayer = new ProjectileUse
            {
                Id = nextId++,
                Shooter = Shooter.Player,
                ItemType = heldItemType,
                StartTick = tick,
                AimPoint = Main.MouseWorld,
                ShooterCentre = Main.LocalPlayer.Center,
                BuffsAtStart = buffs,
            };
        }
        else if (previousAnimation < 0 && itemAnimation > 0 && itemAnimation < itemAnimationMax && openPlayer == null)
        {
            openPlayer = new ProjectileUse
            {
                Id = nextId++,
                Shooter = Shooter.Player,
                ItemType = heldItemType,
                StartTick = tick,
                AimPoint = Main.MouseWorld,
                ShooterCentre = Main.LocalPlayer.Center,
                BuffsAtStart = buffs,
                Complete = false,
            };
        }
        previousAnimation = itemAnimation;
        previousMax = itemAnimationMax;
    }

    /// <summary>A player spawn joins the open use, or the use that parked this tick when the animation already ended. Anything else opens an ad-hoc partial use, so no spawn is ever dropped.</summary>
    public static void NoteSpawn(int tick, int projectileSlot, Projectile projectile, int itemType, int ammoItemId, Vector2 aimPoint)
    {
        ProjectileUse? use = openPlayer ?? pendingPlayer;
        if (use != null && use.ItemType != itemType)
        {
            use.Complete = false;
            Learn(use);
            if (ReferenceEquals(use, openPlayer)) openPlayer = null;
            else pendingPlayer = null;
            use = null;
        }
        use ??= new ProjectileUse
        {
            Id = nextId++,
            Shooter = Shooter.Player,
            ItemType = itemType,
            StartTick = tick,
            AimPoint = aimPoint,
            ShooterCentre = Main.LocalPlayer.Center,
            Complete = false,
        };
        if (!ReferenceEquals(use, openPlayer) && !ReferenceEquals(use, pendingPlayer))
            openPlayer = use;
        use.Spawns.Add(new UseSpawn(projectileSlot, projectile.type, tick, projectile.Center, projectile.velocity,
            projectile.damage, ammoItemId, Main.LocalPlayer.Center, aimPoint));
    }

    /// <summary>A companion firing call opens its use before spawning, with the composition its pellets are measured against.</summary>
    public static int OpenCompanionUse(int tick, int itemType, Vector2 aimPoint, Vector2 shooterCentre, float composedDamage, float composedSpeed)
    {
        if (openCompanion != null)
        {
            openCompanion.Complete = false;
            Learn(openCompanion);
        }
        openCompanion = new ProjectileUse
        {
            Id = nextId++,
            Shooter = Shooter.Companion,
            ItemType = itemType,
            StartTick = tick,
            AimPoint = aimPoint,
            ShooterCentre = shooterCentre,
            ComposedDamage = composedDamage,
            ComposedSpeed = composedSpeed,
        };
        return openCompanion.Id;
    }

    /// <summary>A companion spawn joins its firing call's use.</summary>
    public static void JoinCompanionSpawn(int useId, int tick, int projectileSlot, Projectile projectile)
    {
        if (openCompanion == null || openCompanion.Id != useId) return;
        openCompanion.Spawns.Add(new UseSpawn(projectileSlot, projectile.type, tick, projectile.Center, projectile.velocity,
            projectile.damage, AmmoItemId: 0, openCompanion.ShooterCentre, openCompanion.AimPoint));
    }

    /// <summary>The firing call ended: close its use and teach it. A use that spawned nothing teaches nothing.</summary>
    public static void CloseCompanionUse(int useId, int tick)
    {
        if (openCompanion == null || openCompanion.Id != useId) return;
        ProjectileUse use = openCompanion;
        openCompanion = null;
        use.EndTick = tick;
        if (use.Spawns.Count == 0) return;
        Learn(use);
    }

    public static void Clear()
    {
        nextId = 1;
        openPlayer = null;
        pendingPlayer = null;
        openCompanion = null;
        previousAnimation = -1;
        previousMax = -1;
        closed.Clear();
    }

    private static void FlushPending()
    {
        if (pendingPlayer == null) return;
        Learn(pendingPlayer);
        pendingPlayer = null;
    }

    private static void Learn(ProjectileUse use)
    {
        use.EndTick ??= use.StartTick;
        closed.Add(use);
        while (closed.Count > MaxClosedUsesKept)
            closed.RemoveAt(0);
        var measured = Learning.LearnVolleyShapes.Learn(use);
        // A use with no spawns is a swing or a tool stroke, not a volley: kept for inspection, not recorded.
        if (measured.Count == 0) return;
        var slots = new System.Text.StringBuilder();
        for (int i = 0; i < measured.Count; i++)
        {
            Learning.MeasuredSlot slot = measured[i];
            if (i > 0) slots.Append('|');
            slots.Append(FormattableString.Invariant($"slot={i};type={slot.ProjectileType?.ToString() ?? "-"};angle={slot.AngleFromAimLine:0.000};speed={slot.SpeedRatio:0.000};share={slot.DamageShare:0.000};origin={slot.Origin};delay={slot.DelayTicks}"));
        }
        Diagnostics.GodsEyeEvents.RecordVolleyObserved(use.ItemType, use.Shooter.ToString().ToLowerInvariant(),
            use.Id, use.Complete, use.Spawns.Count, slots.ToString(),
            use.BuffsAtStart.Length == 0 ? "-" : string.Join(",", use.BuffsAtStart));
    }
}
