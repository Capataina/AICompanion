#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using AICompanion.Companion.Brain.Infrastructure.WeaponKnowledge.Learning;
using AICompanion.Companion.Brain.Infrastructure.WeaponKnowledge.Recording;

namespace AICompanion.Companion.Brain.Infrastructure.WeaponKnowledge;

/// <summary>
/// Weapon knowledge as a name-keyed bundle: the one codec behind the player save (phase G) and the
/// combat snapshot's knowledge section (phase D). Every id is a stable name on the wire, so a bundle
/// written under one mod load order resolves under another (row K10); anything naming content that is
/// absent on import is skipped and counted, because knowledge for a missing mod is unusable and would be
/// relearned wrongly against whatever id the name's slot now holds. Floats round-trip exactly, so a
/// restored table prices identically — the audit's fidelity row (A1) depends on it.
/// </summary>
public static class PersistWeaponKnowledge
{
    public const int SchemaVersion = 1;

    private static readonly JsonSerializerOptions Json = new() { IncludeFields = true };

    public sealed record VolleySlotDto(string? Type, float[] Angle, float[] Speed, float[] Share, int Origin,
        float[] Along, float[] Across, float Above, float[] Delay);
    public sealed record VolleyDto(string Item, float[] Count, List<VolleySlotDto> Slots, List<string> Buffs, int Uses);
    public sealed record GravityDto(int Onset, float Accel, float Cap);
    public sealed record DragDto(float X, float Y, int Onset);
    public sealed record SpeedDto(int Onset, float Ratio, float Min, float Max);
    public sealed record HomingDto(int Onset, float Radius, float Blend, float Speed);
    public sealed record SteerDto(int Onset, float Turn);
    public sealed record ReturnDto(int Turn, float Accel, float Max);
    public sealed record WallDto(string Type, int Kind, float RestNormal, float RestTangent, float KeptSpeed,
        int Bounces, bool Retargeting, HomingDto? BounceHoming, int Evidence);
    public sealed record LawDto(string Type, int Revision, int UpdatesPerTick, int Lifetime, GravityDto? Gravity,
        DragDto Drag, SpeedDto? Speed, HomingDto? Homing, SteerDto? Steer, ReturnDto? Return, WallDto Wall,
        float Residual, int Evidence, bool Predictable);
    public sealed record HitDto(string Type, float[] Pierce, List<float[]> Kth, bool Repeats, float AreaRadius,
        int AreaTrigger, int Evidence);
    public sealed record ChildDto(string Type, int Trigger, float[] Period, float[] Count, float[] Along,
        float[] Across, float[] VelAlong, float[] VelAcross, float[] DamageRatio);
    public sealed record ChildrenDto(string Parent, List<ChildDto> Models);
    public sealed record EffectDto(string Item, string Npc, float[] Push, float[] Damage);
    public sealed record OutcomeTypeDto(string Npc, double Precision, double Weighted, double[] Centre,
        double MeanOutcome, int Seen);
    public sealed record OutcomeDto(string Item, double[] Mean, double[] Covariance, int Evidence, double[] Centre,
        int Seen, double TypePriorMean, double TypePriorVariance, List<OutcomeTypeDto> Types);
    public sealed record DebuffDto(string Item, string Npc, int Struck, int Applied, long Ticks);

    public sealed record Bundle(int Version, int KnowledgeRevision, int OutcomeRevision, int EffectsRevision,
        List<VolleyDto> Volleys, List<LawDto> Laws, List<WallDto> Walls, List<HitDto> Hits,
        List<ChildrenDto> Children, List<EffectDto> Effects, List<OutcomeDto> Outcomes, List<DebuffDto> Debuffs);

    /// <summary>
    /// The knowledge in force for these weapons against these enemy types: their volley shapes, the flight laws,
    /// wall, hit and child responses for the projectile closure of those shapes, and the weapon-effects, outcome
    /// and debuff rows for the item-by-enemy pairs that learned any. Default laws and empty tables export nothing,
    /// because the audit and the save rebuild those identically from content. The raw origin geometry behind a
    /// shape's origin kind is deliberately not carried: after a load the kind stands as imported until new uses
    /// re-choose it, which is a brief learning transient, not a pricing difference.
    /// </summary>
    public static string Export(IEnumerable<int> items, IEnumerable<int> npcs, WeaponIdentity? identity = null)
        => JsonSerializer.Serialize(ExportBundle(items, npcs, identity), Json);

    /// <summary>The same bundle unserialized, so the combat snapshot embeds knowledge as a nested object rather than a string.</summary>
    public static Bundle ExportBundle(IEnumerable<int> items, IEnumerable<int> npcs, WeaponIdentity? identity = null)
    {
        identity ??= new WeaponIdentity();
        var itemSet = new HashSet<int>(items);
        var npcSet = new HashSet<int>(npcs);
        var bundle = new Bundle(SchemaVersion, KnowledgeRevision.Current, AttackLearning.Revision, WeaponEffects.Revision,
            new List<VolleyDto>(), new List<LawDto>(), new List<WallDto>(), new List<HitDto>(),
            new List<ChildrenDto>(), new List<EffectDto>(), new List<OutcomeDto>(), new List<DebuffDto>());

        var projectileClosure = new HashSet<int>();
        foreach (int item in itemSet)
        {
            if (!LearnVolleyShapes.Shapes.TryGetValue(item, out VolleyShape? shape))
                continue;
            var slots = new List<VolleySlotDto>();
            foreach (VolleySlot slot in shape.Slots)
            {
                if (slot.ProjectileType is { } slotType)
                    projectileClosure.Add(slotType);
                slots.Add(new VolleySlotDto(
                    slot.ProjectileType is { } named ? identity.NameOfProjectile(named) : null,
                    slot.AngleFromAimLine.Samples.ToArray(), slot.SpeedRatio.Samples.ToArray(),
                    slot.DamageShare.Samples.ToArray(), (int)slot.Origin,
                    slot.OriginAlong.Samples.ToArray(), slot.OriginAcross.Samples.ToArray(),
                    slot.AboveHeight, slot.DelayTicks.Samples.ToArray()));
            }
            var buffs = new List<string>();
            foreach (int buff in shape.LearnedUnder.BuffsSeen)
                buffs.Add(identity.NameOfBuff(buff));
            bundle.Volleys.Add(new VolleyDto(identity.NameOfItem(item), shape.Count.Samples.ToArray(), slots, buffs, shape.UsesLearned));
        }
        ExpandClosure(projectileClosure);
        foreach (int projectile in projectileClosure)
        {
            string name = identity.NameOfProjectile(projectile);
            if (FitFlightLaws.Laws.TryGetValue(projectile, out FlightLaw? law))
                bundle.Laws.Add(ExportLaw(name, law, identity));
            if (LearnWallResponses.Responses.TryGetValue(projectile, out WallResponse? wall))
                bundle.Walls.Add(ExportWall(name, wall));
            if (LearnHitResponses.Responses.TryGetValue(projectile, out HitResponse? hit))
                bundle.Hits.Add(ExportHit(name, hit));
            if (LearnChildSpawns.Children.TryGetValue(projectile, out List<ChildModel>? models))
            {
                var exported = new List<ChildDto>();
                foreach (ChildModel model in models)
                    exported.Add(new ChildDto(identity.NameOfProjectile(model.ChildType), (int)model.Trigger,
                        model.Period.Samples.ToArray(), model.Count.Samples.ToArray(),
                        model.OffsetAlong.Samples.ToArray(), model.OffsetAcross.Samples.ToArray(),
                        model.VelocityAlong.Samples.ToArray(), model.VelocityAcross.Samples.ToArray(),
                        model.DamageRatio.Samples.ToArray()));
                bundle.Children.Add(new ChildrenDto(name, exported));
            }
        }
        foreach (var (pair, samples) in WeaponEffects.ExportRecords())
        {
            if (!itemSet.Contains(pair.Item) || !npcSet.Contains(pair.Npc))
                continue;
            bundle.Effects.Add(new EffectDto(identity.NameOfItem(pair.Item), identity.NameOfNpc(pair.Npc), samples.Push, samples.Damage));
        }
        foreach (int item in itemSet)
        {
            AttackLearning.ExportedModel? model = AttackLearning.ExportModel(item);
            if (model == null)
                continue;
            var types = new List<OutcomeTypeDto>();
            foreach ((int npcType, double precision, double weighted, double[] centre, double meanOutcome, int seen) in model.Types)
            {
                if (!npcSet.Contains(npcType))
                    continue;
                types.Add(new OutcomeTypeDto(identity.NameOfNpc(npcType), precision, weighted, centre, meanOutcome, seen));
            }
            bundle.Outcomes.Add(new OutcomeDto(identity.NameOfItem(item), model.Mean, model.Covariance, model.Evidence,
                model.Centre, model.Seen, model.TypePriorMean, model.TypePriorVariance, types));
        }
        foreach (var (pair, counts) in AttackLearning.ExportDebuffs())
        {
            if (!itemSet.Contains(pair.Item) || !npcSet.Contains(pair.Npc))
                continue;
            bundle.Debuffs.Add(new DebuffDto(identity.NameOfItem(pair.Item), identity.NameOfNpc(pair.Npc),
                counts.Struck, counts.Applied, counts.Ticks));
        }
        return bundle;
    }

    /// <summary>Everything learned, for the player save: every shaped item, every enemy type any table names.</summary>
    public static string ExportAll(WeaponIdentity? identity = null)
    {
        var items = new HashSet<int>(LearnVolleyShapes.Shapes.Keys);
        foreach (int item in AttackLearning.LearnedItems)
            items.Add(item);
        var npcs = new HashSet<int>();
        foreach (var (pair, _) in WeaponEffects.ExportRecords())
        {
            items.Add(pair.Item);
            npcs.Add(pair.Npc);
        }
        foreach (var (pair, _) in AttackLearning.ExportDebuffs())
        {
            items.Add(pair.Item);
            npcs.Add(pair.Npc);
        }
        foreach (int item in items)
        {
            AttackLearning.ExportedModel? model = AttackLearning.ExportModel(item);
            if (model == null)
                continue;
            foreach ((int npcType, _, _, _, _, _) in model.Types)
                npcs.Add(npcType);
        }
        return Export(items, npcs, identity);
    }

    /// <summary>
    /// Install a bundle, resolving every name against the content now loaded. Entries naming absent content are
    /// skipped and counted rather than failing the load: a save made with a weapon mod still loads without it,
    /// and the knowledge for what is present is exact. Returns the entries installed and skipped.
    /// </summary>
    public static (int Installed, int Skipped) Import(string json, WeaponIdentity? identity = null)
    {
        identity ??= new WeaponIdentity();
        Bundle? bundle;
        try
        {
            bundle = JsonSerializer.Deserialize<Bundle>(json, Json);
        }
        catch (Exception)
        {
            return (0, 0);
        }
        return ImportBundle(bundle, identity);
    }

    /// <summary>The same install from an unserialized bundle, for the snapshot's nested knowledge.</summary>
    public static (int Installed, int Skipped) ImportBundle(Bundle? bundle, WeaponIdentity? identity = null)
    {
        identity ??= new WeaponIdentity();
        if (bundle == null || bundle.Version != SchemaVersion)
            return (0, 0);
        int installed = 0, skipped = 0;
        foreach (VolleyDto volley in bundle.Volleys)
        {
            int? item = identity.ItemOfName(volley.Item);
            if (item == null) { skipped++; continue; }
            var shape = new VolleyShape { UsesLearned = volley.Uses };
            shape.Count.AddMany(volley.Count);
            bool badSlot = false;
            foreach (VolleySlotDto slot in volley.Slots)
            {
                int? slotType = null;
                if (slot.Type != null)
                {
                    slotType = identity.ProjectileOfName(slot.Type);
                    if (slotType == null) { badSlot = true; break; }
                }
                if (!Enum.IsDefined(typeof(OriginKind), slot.Origin)) { badSlot = true; break; }
                var restored = new VolleySlot
                {
                    ProjectileType = slotType,
                    Origin = (OriginKind)slot.Origin,
                    AboveHeight = slot.Above,
                };
                restored.AngleFromAimLine.AddMany(slot.Angle);
                restored.SpeedRatio.AddMany(slot.Speed);
                restored.DamageShare.AddMany(slot.Share);
                restored.OriginAlong.AddMany(slot.Along);
                restored.OriginAcross.AddMany(slot.Across);
                restored.DelayTicks.AddMany(slot.Delay);
                shape.Slots.Add(restored);
            }
            if (badSlot) { skipped++; continue; }
            foreach (string buff in volley.Buffs)
            {
                int? buffId = identity.BuffOfName(buff);
                if (buffId != null)
                    shape.LearnedUnder.BuffsSeen.Add(buffId.Value);
            }
            LearnVolleyShapes.AssumeShape(item.Value, shape);
            installed++;
        }
        foreach (LawDto law in bundle.Laws)
        {
            int? type = identity.ProjectileOfName(law.Type);
            FlightLaw? restored = type == null ? null : ImportLaw(type.Value, law, identity);
            if (restored == null) { skipped++; continue; }
            FitFlightLaws.AssumeLaw(type!.Value, restored);
            installed++;
        }
        foreach (WallDto wall in bundle.Walls)
        {
            int? type = identity.ProjectileOfName(wall.Type);
            WallResponse? restored = type == null ? null : ImportWall(type.Value, wall);
            if (restored == null) { skipped++; continue; }
            LearnWallResponses.AssumeResponse(restored);
            installed++;
        }
        foreach (HitDto hit in bundle.Hits)
        {
            int? type = identity.ProjectileOfName(hit.Type);
            if (type == null || !Enum.IsDefined(typeof(AreaTrigger), hit.AreaTrigger)) { skipped++; continue; }
            var restored = new HitResponse
            {
                ProjectileType = type.Value,
                RepeatHitsObserved = hit.Repeats,
                Area = new AreaResponse(hit.AreaRadius, (AreaTrigger)hit.AreaTrigger),
                Evidence = hit.Evidence,
            };
            restored.PierceRatio.AddMany(hit.Pierce);
            foreach (float[] kth in hit.Kth)
            {
                var distribution = new Distribution();
                distribution.AddMany(kth);
                restored.KthHitRatio.Add(distribution);
            }
            LearnHitResponses.AssumeResponse(restored);
            installed++;
        }
        foreach (ChildrenDto children in bundle.Children)
        {
            int? parent = identity.ProjectileOfName(children.Parent);
            if (parent == null) { skipped++; continue; }
            var models = new List<ChildModel>();
            bool badChild = false;
            foreach (ChildDto child in children.Models)
            {
                int? childType = identity.ProjectileOfName(child.Type);
                if (childType == null || !Enum.IsDefined(typeof(ChildTrigger), child.Trigger)) { badChild = true; break; }
                var model = new ChildModel { ChildType = childType.Value, Trigger = (ChildTrigger)child.Trigger };
                model.Period.AddMany(child.Period);
                model.Count.AddMany(child.Count);
                model.OffsetAlong.AddMany(child.Along);
                model.OffsetAcross.AddMany(child.Across);
                model.VelocityAlong.AddMany(child.VelAlong);
                model.VelocityAcross.AddMany(child.VelAcross);
                model.DamageRatio.AddMany(child.DamageRatio);
                models.Add(model);
            }
            if (badChild) { skipped++; continue; }
            LearnChildSpawns.AssumeChildren(parent.Value, models);
            installed++;
        }
        foreach (EffectDto effect in bundle.Effects)
        {
            int? item = identity.ItemOfName(effect.Item);
            int? npc = identity.NpcOfName(effect.Npc);
            if (item == null || npc == null) { skipped++; continue; }
            WeaponEffects.AssumeRecord(item.Value, npc.Value, effect.Push, effect.Damage);
            installed++;
        }
        foreach (OutcomeDto outcome in bundle.Outcomes)
        {
            int? item = identity.ItemOfName(outcome.Item);
            if (item == null) { skipped++; continue; }
            var types = new List<(int NpcType, double Precision, double Weighted, double[] Centre, double MeanOutcome, int Seen)>();
            foreach (OutcomeTypeDto type in outcome.Types)
            {
                int? npc = identity.NpcOfName(type.Npc);
                if (npc == null)
                    continue;
                types.Add((npc.Value, type.Precision, type.Weighted, type.Centre, type.MeanOutcome, type.Seen));
            }
            AttackLearning.AssumeModel(item.Value, new AttackLearning.ExportedModel(outcome.Mean, outcome.Covariance,
                outcome.Evidence, outcome.Centre, outcome.Seen, outcome.TypePriorMean, outcome.TypePriorVariance, types));
            installed++;
        }
        foreach (DebuffDto debuff in bundle.Debuffs)
        {
            int? item = identity.ItemOfName(debuff.Item);
            int? npc = identity.NpcOfName(debuff.Npc);
            if (item == null || npc == null) { skipped++; continue; }
            AttackLearning.AssumeDebuff(item.Value, npc.Value, debuff.Struck, debuff.Applied, debuff.Ticks);
            installed++;
        }
        KnowledgeRevision.Restore(bundle.KnowledgeRevision);
        AttackLearning.RestoreRevision(bundle.OutcomeRevision);
        WeaponEffects.RestoreRevision(bundle.EffectsRevision);
        return (installed, skipped);
    }

    private static void ExpandClosure(HashSet<int> projectiles)
    {
        var queue = new Queue<int>(projectiles);
        while (queue.TryDequeue(out int parent))
            foreach (ChildModel model in LearnChildSpawns.ChildrenFor(parent))
                if (projectiles.Add(model.ChildType))
                    queue.Enqueue(model.ChildType);
    }

    private static LawDto ExportLaw(string name, FlightLaw law, WeaponIdentity identity)
    {
        return new LawDto(name, law.Revision, law.UpdatesPerTick, law.LifetimeUpdates,
            law.Gravity is { } gravity ? new GravityDto(gravity.OnsetUpdate, gravity.Acceleration, gravity.MaxFallSpeed) : null,
            new DragDto(law.Drag.Horizontal, law.Drag.Vertical, law.Drag.OnsetUpdate),
            law.SpeedChange is { } speed ? new SpeedDto(speed.OnsetUpdate, speed.RatioPerUpdate, speed.MinSpeed, speed.MaxSpeed) : null,
            law.Homing is { } homing ? new HomingDto(homing.OnsetUpdate, homing.Radius, homing.Blend, homing.Speed) : null,
            law.SteerToAim is { } steer ? new SteerDto(steer.OnsetUpdate, steer.MaxTurnPerUpdate) : null,
            law.Return is { } ret ? new ReturnDto(ret.TurnUpdate, ret.Acceleration, ret.MaxSpeed) : null,
            ExportWall(identity.NameOfProjectile(law.Wall.ProjectileType), law.Wall),
            law.ResidualPerUpdate, law.Evidence, law.Predictable);
    }

    private static WallDto ExportWall(string name, WallResponse wall)
        => new(name, (int)wall.Kind, wall.RestitutionNormal, wall.RestitutionTangent, wall.SpeedFactorAfterBounce,
            wall.BounceCount, wall.RetargetingBounce,
            wall.BounceHoming is { } homing ? new HomingDto(homing.OnsetUpdate, homing.Radius, homing.Blend, homing.Speed) : null,
            wall.Evidence);

    private static HitDto ExportHit(string name, HitResponse hit)
    {
        var kth = new List<float[]>();
        foreach (Distribution distribution in hit.KthHitRatio)
            kth.Add(distribution.Samples.ToArray());
        return new HitDto(name, hit.PierceRatio.Samples.ToArray(), kth, hit.RepeatHitsObserved,
            hit.Area.Radius, (int)hit.Area.Trigger, hit.Evidence);
    }

    private static FlightLaw? ImportLaw(int type, LawDto law, WeaponIdentity identity)
    {
        int? wallType = identity.ProjectileOfName(law.Wall.Type);
        WallResponse? wall = wallType == null ? null : ImportWall(wallType.Value, law.Wall);
        if (wall == null)
            return null;
        return new FlightLaw(type, law.Revision, law.UpdatesPerTick, law.Lifetime,
            law.Gravity is null ? null : new GravityTerm(law.Gravity.Onset, law.Gravity.Accel, law.Gravity.Cap),
            new DragTerm(law.Drag.X, law.Drag.Y, law.Drag.Onset),
            law.Speed is null ? null : new SpeedChangeTerm(law.Speed.Onset, law.Speed.Ratio, law.Speed.Min, law.Speed.Max),
            law.Homing is null ? null : new HomingTerm(law.Homing.Onset, law.Homing.Radius, HomingAnchor.NearestNpc, law.Homing.Blend, law.Homing.Speed),
            law.Steer is null ? null : new SteerToAimTerm(law.Steer.Onset, law.Steer.Turn),
            law.Return is null ? null : new ReturnToOwnerTerm(law.Return.Turn, law.Return.Accel, law.Return.Max),
            wall, law.Residual, law.Evidence, law.Predictable);
    }

    private static WallResponse? ImportWall(int type, WallDto wall)
    {
        if (!Enum.IsDefined(typeof(WallKind), wall.Kind))
            return null;
        return new WallResponse
        {
            ProjectileType = type,
            Kind = (WallKind)wall.Kind,
            RestitutionNormal = wall.RestNormal,
            RestitutionTangent = wall.RestTangent,
            SpeedFactorAfterBounce = wall.KeptSpeed,
            BounceCount = wall.Bounces,
            RetargetingBounce = wall.Retargeting,
            BounceHoming = wall.BounceHoming is null ? null
                : new HomingTerm(wall.BounceHoming.Onset, wall.BounceHoming.Radius, HomingAnchor.NearestNpc,
                    wall.BounceHoming.Blend, wall.BounceHoming.Speed),
            Evidence = wall.Evidence,
        };
    }
}
