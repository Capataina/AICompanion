#nullable enable

using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace AICompanion.Companion.Brain.Infrastructure.WeaponKnowledge.Learning;

/// <summary>Where a volley slot's projectiles come from: the shooter's muzzle, the aim point, a fixed height above it, or around the shooter regardless of aim.</summary>
public enum OriginKind { Shooter, AtAim, AboveAim, AroundShooter }

/// <summary>One slot's projectiles as the companion will fire them: which type, from where, how fast, at what
/// damage, and on which tick of the use. Live fire emits same-tick and ignores the delay; the simulator times it.</summary>
public readonly record struct VolleySpawn(int ProjectileType, Vector2 Position, Vector2 Velocity, int Damage, int DelayTicks);

/// <summary>One use's measured decomposition into slots, for the god's-eye record: what this use's spawns looked like before any learning.</summary>
public readonly record struct MeasuredSlot(int? ProjectileType, float AngleFromAimLine, float SpeedRatio, float DamageShare,
    OriginKind Origin, float OriginAlong, float OriginAcross, int DelayTicks);

/// <summary>A bounded sample of floats with the quantiles the volley reader needs. Medians throughout, so one strange use cannot drag a slot.</summary>
public sealed class Distribution
{
    /// <summary>How many samples a distribution keeps; older uses leave the window as new ones arrive.</summary>
    public const int MaxSamples = 16;

    private readonly List<float> samples = new();

    public int Count => samples.Count;

    public void Add(float value)
    {
        if (!float.IsFinite(value)) return;
        samples.Add(value);
        while (samples.Count > MaxSamples)
            samples.RemoveAt(0);
    }

    public void AddMany(IEnumerable<float> values)
    {
        foreach (float value in values)
            Add(value);
    }

    /// <summary>The median, or <paramref name="fallback"/> with no samples.</summary>
    public float Median(float fallback = 0f)
    {
        if (samples.Count == 0) return fallback;
        var sorted = new List<float>(samples);
        sorted.Sort();
        int n = sorted.Count;
        return n % 2 == 1 ? sorted[n / 2] : (sorted[n / 2 - 1] + sorted[n / 2]) / 2f;
    }

    public float Variance()
    {
        if (samples.Count < 2) return 0f;
        float mean = 0f;
        foreach (float value in samples)
            mean += value;
        mean /= samples.Count;
        float sum = 0f;
        foreach (float value in samples)
            sum += (value - mean) * (value - mean);
        return sum / samples.Count;
    }

    public IReadOnlyList<float> Samples => samples;
}

/// <summary>
/// One volley slot: the same-positioned projectile across uses, ordered by spawn delay then angle. A slot whose
/// item uses ammo keeps no type and is substituted from the companion's ammo at fire time; a slot whose observed
/// type is not its ammo's keeps it, which is what a use mixing fixed and ammo projectiles needs.
/// </summary>
public sealed class VolleySlot
{
    public int? ProjectileType;
    public Distribution AngleFromAimLine = new();
    public Distribution SpeedRatio = new();
    public Distribution DamageShare = new();
    public OriginKind Origin = OriginKind.Shooter;
    public Distribution OriginAlong = new();
    public Distribution OriginAcross = new();
    /// <summary>For <see cref="OriginKind.AboveAim"/>: the slot's median rain height above the aim point. The anchor "a fixed height above the cursor" needs the height from somewhere, and this is it.</summary>
    public float AboveHeight;
    public Distribution DelayTicks = new();

    /// <summary>Raw spawn geometry kept so the origin kind is re-chosen by smallest variance on every use rather than frozen by the first.</summary>
    public readonly List<(Vector2 Shooter, Vector2 Aim, Vector2 Position)> OriginSamples = new();
}

/// <summary>Under which count-changing buffs a shape's counts were learned, and how many uses were set aside for being under ones the companion lacks.</summary>
public sealed class MultishotState
{
    public readonly HashSet<int> BuffsSeen = new();
    public int UsesSetAside;
}

/// <summary>
/// What one use of one item puts into the world, learned from the player's own uses. The count is a distribution
/// over complete uses only — a use joined mid-animation is short and must not teach how many a use holds — while
/// slots learn from every use with spawns, partial or whole.
/// </summary>
public sealed class VolleyShape
{
    public Distribution Count = new();
    public readonly List<VolleySlot> Slots = new();
    public MultishotState LearnedUnder = new();
    public int UsesLearned;

    /// <summary>
    /// The volley as the companion fires it: the median count of slots, each at its slots' medians. Fixed
    /// quantiles, so the same shape fires the same volley every time and the simulator in phase C reads the
    /// same medians. Delays are learned and recorded but live fire emits same-tick: reproducing a burst's
    /// timing needs a cross-tick spawn queue behind a moving muzzle, and the damage a use puts out is the same.
    /// </summary>
    public IReadOnlyList<VolleySpawn> Expand(Vector2 muzzle, Vector2 aimPoint, Vector2 aimDirection, int typeFallback,
        float composedDamage, float composedSpeed)
    {
        if (Slots.Count == 0)
            return Array.Empty<VolleySpawn>();
        int count = Math.Clamp((int)MathF.Round(Count.Median(Slots.Count)), 1, Slots.Count);
        Vector2 aim = aimDirection == Vector2.Zero ? Vector2.UnitX : Vector2.Normalize(aimDirection);
        Vector2 across = new(-aim.Y, aim.X);
        var spawns = new List<VolleySpawn>(count);
        for (int i = 0; i < count; i++)
        {
            VolleySlot slot = Slots[i];
            Vector2 direction = Rotate(aim, slot.AngleFromAimLine.Median());
            float speed = composedSpeed * slot.SpeedRatio.Median(1f);
            Vector2 anchor = slot.Origin switch
            {
                OriginKind.AtAim => aimPoint,
                OriginKind.AboveAim => aimPoint + new Vector2(0f, -slot.AboveHeight),
                OriginKind.AroundShooter or OriginKind.Shooter => muzzle,
                _ => muzzle,
            };
            Vector2 position = slot.Origin == OriginKind.AroundShooter
                ? anchor + new Vector2(slot.OriginAlong.Median(), slot.OriginAcross.Median())
                : anchor + aim * slot.OriginAlong.Median() + across * slot.OriginAcross.Median();
            // The epsilon is DamagePerHit's: a share recomposed against its own composition can land an
            // ulp under the integer, and truncation would read a quarter of twenty as four.
            int damage = Math.Max(0, (int)(composedDamage * slot.DamageShare.Median(1f) + 5E-06f));
            spawns.Add(new VolleySpawn(slot.ProjectileType ?? typeFallback, position, direction * speed, damage,
                Math.Max(0, (int)slot.DelayTicks.Median())));
        }
        return spawns;
    }

    private static Vector2 Rotate(Vector2 vector, float radians)
    {
        float cos = MathF.Cos(radians), sin = MathF.Sin(radians);
        return new Vector2(vector.X * cos - vector.Y * sin, vector.X * sin + vector.Y * cos);
    }
}

/// <summary>
/// Volley shapes from grouped uses. Shapes learn only from the player's uses: the companion's own volleys are
/// reproductions of the learned medians, and feeding them back would narrow every distribution toward what the
/// companion already fires. Companion uses are still measured, so the god's-eye record says what every use held.
/// A use with no spawns — a sword swing, a tool stroke — teaches nothing and records nothing: it is not a volley.
/// </summary>
public static class LearnVolleyShapes
{
    /// <summary>
    /// Buffs that change how many projectiles a use holds, whose samples are kept apart because the companion —
    /// an NPC with no buffs — never fires under them. Empty: no vanilla buff is verified to change a count
    /// (archery and quivers change arrow damage and speed, which the shares already absorb; the Hive Pack's extra
    /// bees are an accessory, not a buff). A fixture classifies a synthetic id to exercise the keep-apart path.
    /// </summary>
    public static readonly HashSet<int> CountChangingBuffs = new();

    private static readonly Dictionary<int, VolleyShape> shapes = new();

    /// <summary>The shape for an item: learned if any player use taught it, else the default single straight shot the hand fires today.</summary>
    public static VolleyShape ShapeFor(int itemType)
    {
        if (shapes.TryGetValue(itemType, out VolleyShape? shape))
            return shape;
        var single = new VolleyShape();
        single.Count.Add(1f);
        single.Slots.Add(new VolleySlot());
        return single;
    }

    /// <summary>Whether any player use has taught this item a shape yet.</summary>
    public static bool HasShape(int itemType) => shapes.ContainsKey(itemType);

    /// <summary>
    /// How wide the learned volley opens around the aim line: the furthest any slot's median angle sits from it.
    /// Zero for an unobserved item, whose single shot has no spread. The residual learner scales aim offsets by
    /// this, so "off the intercept" means the same share on a shotgun and on a sniper.
    /// </summary>
    public static float SpreadCone(int itemType)
    {
        if (!shapes.TryGetValue(itemType, out VolleyShape? shape)) return 0f;
        float cone = 0f;
        foreach (VolleySlot slot in shape.Slots)
            cone = MathF.Max(cone, MathF.Abs(slot.AngleFromAimLine.Median()));
        return cone;
    }

    /// <summary>
    /// Teach one grouped use and return its measured slots for the record. Partial uses teach their slots but
    /// not their count; uses under count-changing buffs the companion lacks are counted aside and teach neither.
    /// </summary>
    public static IReadOnlyList<MeasuredSlot> Learn(Recording.ProjectileUse use)
    {
        if (use.Spawns.Count == 0)
            return Array.Empty<MeasuredSlot>();
        shapes.TryGetValue(use.ItemType, out VolleyShape? shape);
        if (use.Shooter != Recording.Shooter.Player)
            return Measure(use, shape);
        if (shape == null)
            shapes[use.ItemType] = shape = new VolleyShape();
        foreach (int buff in use.BuffsAtStart)
            shape.LearnedUnder.BuffsSeen.Add(buff);
        foreach (int buff in use.BuffsAtStart)
        {
            if (CountChangingBuffs.Contains(buff))
            {
                shape.LearnedUnder.UsesSetAside++;
                return Measure(use, shape);
            }
        }
        var ordered = OrderedSpawns(use);
        (float damage, float speed) = Composed(use.ItemType, use);
        bool usesAmmo = UsesAmmo(use.ItemType);
        for (int i = 0; i < ordered.Count; i++)
        {
            Recording.UseSpawn spawn = ordered[i];
            while (shape.Slots.Count <= i)
                shape.Slots.Add(new VolleySlot());
            VolleySlot slot = shape.Slots[i];
            Vector2 aimDirection = AimDirection(use);
            float angle = AngleFromAim(spawn.Velocity, aimDirection);
            slot.AngleFromAimLine.Add(angle);
            slot.SpeedRatio.Add(speed > 0f ? spawn.Velocity.Length() / speed : 1f);
            slot.DamageShare.Add(damage > 0f ? spawn.Damage / damage : 1f);
            slot.DelayTicks.Add(spawn.Tick - use.StartTick);
            if (slot.ProjectileType == null)
                slot.ProjectileType = SlotType(use.ItemType, spawn, usesAmmo);
            slot.OriginSamples.Add((spawn.ShooterCentre, spawn.AimPoint, spawn.Position));
            while (slot.OriginSamples.Count > Distribution.MaxSamples)
                slot.OriginSamples.RemoveAt(0);
            RefitOrigin(slot);
        }
        if (use.Complete)
            shape.Count.Add(use.Spawns.Count);
        shape.UsesLearned++;
        KnowledgeRevision.Bump();
        return Measure(use, shape);
    }

    private static List<Recording.UseSpawn> OrderedSpawns(Recording.ProjectileUse use)
    {
        var ordered = new List<Recording.UseSpawn>(use.Spawns);
        Vector2 aimDirection = AimDirection(use);
        ordered.Sort((a, b) =>
        {
            int byTick = a.Tick.CompareTo(b.Tick);
            return byTick != 0 ? byTick : AngleFromAim(a.Velocity, aimDirection).CompareTo(AngleFromAim(b.Velocity, aimDirection));
        });
        return ordered;
    }

    private static Vector2 AimDirection(Recording.ProjectileUse use)
    {
        Vector2 aim = use.AimPoint - use.ShooterCentre;
        return aim == Vector2.Zero ? Vector2.UnitX : Vector2.Normalize(aim);
    }

    private static float AngleFromAim(Vector2 velocity, Vector2 aimDirection)
    {
        if (velocity == Vector2.Zero || aimDirection == Vector2.Zero) return 0f;
        return MathHelper.WrapAngle(velocity.ToRotation() - aimDirection.ToRotation());
    }

    private static int? SlotType(int itemType, Recording.UseSpawn spawn, bool usesAmmo)
    {
        if (!usesAmmo)
            return spawn.ProjectileType;
        // An ammo weapon's own ammo is substituted at fire time; a type that is not its ammo's is fixed and kept.
        if (ContentSamples.ItemsByType.TryGetValue(spawn.AmmoItemId, out Item? ammo) && ammo.shoot == spawn.ProjectileType)
            return null;
        if (ContentSamples.ItemsByType.TryGetValue(itemType, out Item? item)
            && Inventory.CompanionGear.DefaultAmmo(item) is { shoot: > 0 } fallback && fallback.shoot == spawn.ProjectileType)
            return null;
        return spawn.ProjectileType;
    }

    private static bool UsesAmmo(int itemType)
        => ContentSamples.ItemsByType.TryGetValue(itemType, out Item? item) && item.useAmmo != 0;

    /// <summary>
    /// The item's composed damage and launch speed as the hand fires them: the item plus the named ammo, with the
    /// player's live class modifiers, but no mana factor — mana scales the companion's cast, not the player's. The
    /// shares are measured against this, so what the formula omits (a damage potion drunk mid-swing) the shares absorb.
    /// </summary>
    public static (float Damage, float Speed) ComposedStats(int itemType, int ammoItemId)
    {
        if (!ContentSamples.ItemsByType.TryGetValue(itemType, out Item? item) || item == null)
            return (0f, 0f);
        Item? ammo = null;
        if (ammoItemId != 0)
            ContentSamples.ItemsByType.TryGetValue(ammoItemId, out ammo);
        ammo ??= Inventory.CompanionGear.DefaultAmmo(item);
        float speed = item.shootSpeed + (ammo?.shootSpeed ?? 0f);
        Player player = Main.LocalPlayer;
        StatModifier damage = player.GetTotalDamage(item.DamageType);
        if (ammo != null && AmmoID.Sets.IsArrow[ammo.ammo]) damage = damage.CombineWith(player.arrowDamage);
        if (ammo != null && AmmoID.Sets.IsBullet[ammo.ammo]) damage = damage.CombineWith(player.bulletDamage);
        if (ammo != null && AmmoID.Sets.IsSpecialist[ammo.ammo]) damage = damage.CombineWith(player.specialistDamage);
        return (damage.ApplyTo(item.damage + (ammo?.damage ?? 0)), speed);
    }

    /// <summary>One use's composition, from the ammo its first spawn names. Read at the use's closing, a tick after
    /// its last spawn, which is the closest the grouping gets to "at that moment".</summary>
    private static (float Damage, float Speed) Composed(int itemType, Recording.ProjectileUse use)
    {
        int ammoItemId = 0;
        foreach (Recording.UseSpawn spawn in use.Spawns)
        {
            if (spawn.AmmoItemId != 0)
            {
                ammoItemId = spawn.AmmoItemId;
                break;
            }
        }
        return ComposedStats(itemType, ammoItemId);
    }

    /// <summary>
    /// The origin kind is whichever anchor leaves the smallest offset variance: the shooter's centre in the aim
    /// frame, the aim point in the aim frame, a learned height above the aim point in the aim frame, or the
    /// shooter's centre in the world frame. The sky-rain case is found without being named; a rain height under
    /// two tiles is the aim point wearing a hat and is not candidated.
    /// </summary>
    private static void RefitOrigin(VolleySlot slot)
    {
        var samples = slot.OriginSamples;
        if (samples.Count == 0) return;
        var heights = new List<float>(samples.Count);
        foreach ((_, Vector2 aim, Vector2 position) in samples)
            heights.Add(aim.Y - position.Y);
        heights.Sort();
        float height = heights.Count % 2 == 1 ? heights[heights.Count / 2] : (heights[heights.Count / 2 - 1] + heights[heights.Count / 2]) / 2f;
        slot.AboveHeight = height;

        float shooter = VarianceOf(samples, s => InAimFrame(s.Position - s.Shooter, AimOf(s)));
        float atAim = VarianceOf(samples, s => InAimFrame(s.Position - s.Aim, AimOf(s)));
        float aboveAim = height >= 32f
            ? VarianceOf(samples, s => InAimFrame(s.Position - (s.Aim + new Vector2(0f, -height)), AimOf(s)))
            : float.MaxValue;
        float around = VarianceOf(samples, s => s.Position - s.Shooter);

        const float tie = 1e-6f;
        OriginKind kind = OriginKind.Shooter;
        float best = shooter;
        if (atAim < best - tie) { kind = OriginKind.AtAim; best = atAim; }
        if (aboveAim < best - tie) { kind = OriginKind.AboveAim; best = aboveAim; }
        if (around < best - tie) { kind = OriginKind.AroundShooter; best = around; }
        slot.Origin = kind;

        slot.OriginAlong = new Distribution();
        slot.OriginAcross = new Distribution();
        foreach ((Vector2 shooterCentre, Vector2 aimPoint, Vector2 position) in samples)
        {
            Vector2 aimDirection = aimPoint - shooterCentre;
            aimDirection = aimDirection == Vector2.Zero ? Vector2.UnitX : Vector2.Normalize(aimDirection);
            Vector2 offset = kind switch
            {
                OriginKind.AtAim => position - aimPoint,
                OriginKind.AboveAim => position - (aimPoint + new Vector2(0f, -height)),
                OriginKind.AroundShooter => position - shooterCentre,
                _ => position - shooterCentre,
            };
            if (kind == OriginKind.AroundShooter)
            {
                slot.OriginAlong.Add(offset.X);
                slot.OriginAcross.Add(offset.Y);
            }
            else
            {
                Vector2 across = new(-aimDirection.Y, aimDirection.X);
                slot.OriginAlong.Add(Vector2.Dot(offset, aimDirection));
                slot.OriginAcross.Add(Vector2.Dot(offset, across));
            }
        }
    }

    private static Vector2 InAimFrame(Vector2 offset, Vector2 aimDirection)
    {
        Vector2 across = new(-aimDirection.Y, aimDirection.X);
        return new Vector2(Vector2.Dot(offset, aimDirection), Vector2.Dot(offset, across));
    }

    private static Vector2 AimOf((Vector2 Shooter, Vector2 Aim, Vector2 Position) sample)
    {
        Vector2 aim = sample.Aim - sample.Shooter;
        return aim == Vector2.Zero ? Vector2.UnitX : Vector2.Normalize(aim);
    }

    private static float VarianceOf(List<(Vector2 Shooter, Vector2 Aim, Vector2 Position)> samples,
        Func<(Vector2 Shooter, Vector2 Aim, Vector2 Position), Vector2> project)
    {
        var xs = new List<float>(samples.Count);
        var ys = new List<float>(samples.Count);
        foreach (var sample in samples)
        {
            Vector2 projected = project(sample);
            xs.Add(projected.X);
            ys.Add(projected.Y);
        }
        return VarianceOf(xs) + VarianceOf(ys);
    }

    private static float VarianceOf(List<float> values)
    {
        if (values.Count < 2) return 0f;
        float mean = 0f;
        foreach (float value in values)
            mean += value;
        mean /= values.Count;
        float sum = 0f;
        foreach (float value in values)
            sum += (value - mean) * (value - mean);
        return sum / values.Count;
    }

    private static IReadOnlyList<MeasuredSlot> Measure(Recording.ProjectileUse use, VolleyShape? shape)
    {
        if (use.Spawns.Count == 0)
            return Array.Empty<MeasuredSlot>();
        var ordered = OrderedSpawns(use);
        (float damage, float speed) = Composed(use.ItemType, use);
        Vector2 aimDirection = AimDirection(use);
        bool usesAmmo = UsesAmmo(use.ItemType);
        var measured = new List<MeasuredSlot>(ordered.Count);
        for (int i = 0; i < ordered.Count; i++)
        {
            Recording.UseSpawn spawn = ordered[i];
            VolleySlot? slot = shape != null && i < shape.Slots.Count ? shape.Slots[i] : null;
            measured.Add(new MeasuredSlot(SlotType(use.ItemType, spawn, usesAmmo),
                AngleFromAim(spawn.Velocity, aimDirection),
                speed > 0f ? spawn.Velocity.Length() / speed : 1f,
                damage > 0f ? spawn.Damage / damage : 1f,
                slot?.Origin ?? OriginKind.Shooter,
                slot?.OriginAlong.Median() ?? 0f, slot?.OriginAcross.Median() ?? 0f,
                spawn.Tick - use.StartTick));
        }
        return measured;
    }

    /// <summary>Forget every shape, so a fixture measures learning rather than the previous case's.</summary>
    public static void Reset()
    {
        shapes.Clear();
        // Forgetting is a belief change like any other: the sim cache keys on the revision, so a reset that left
        // it standing would serve sims priced under the forgotten shapes.
        KnowledgeRevision.Bump();
    }
}
