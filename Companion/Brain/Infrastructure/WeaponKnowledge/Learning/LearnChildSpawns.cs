#nullable enable

using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace AICompanion.Companion.Brain.Infrastructure.WeaponKnowledge.Learning;

/// <summary>
/// One child type's learned spawn model: what triggers it, the timer period when the trigger is time, how many
/// each event holds, and offsets and velocities in the parent's velocity frame so they rotate with the shot.
/// </summary>
public sealed class ChildModel
{
    public int ChildType;
    public Recording.ChildTrigger Trigger;
    public readonly Distribution Period = new();
    public readonly Distribution Count = new();
    public readonly Distribution OffsetAlong = new();
    public readonly Distribution OffsetAcross = new();
    public readonly Distribution VelocityAlong = new();
    public readonly Distribution VelocityAcross = new();
    public readonly Distribution DamageRatio = new();
}

/// <summary>
/// Child spawns from parent traces, grouped by child type. The trigger is the coincidence the recorder filed —
/// a body hit, a wall contact, the parent's death, or a timer whose period is the median gap between spawns.
/// Each child flies its own learned law, to the depth of chain observed, which is the simulator's recursion.
/// </summary>
public static class LearnChildSpawns
{
    private static readonly Dictionary<int, List<ChildModel>> children = new();

    public static IReadOnlyList<ChildModel> ChildrenFor(int parentType)
        => children.TryGetValue(parentType, out List<ChildModel>? models) ? models : Array.Empty<ChildModel>();

    public static void Learn(Recording.FlightTrace trace)
    {
        if (trace.Children.Count == 0) return;
        KnowledgeRevision.Bump();
        if (!children.TryGetValue(trace.ProjectileType, out List<ChildModel>? models))
            children[trace.ProjectileType] = models = new List<ChildModel>();
        var byType = new Dictionary<int, List<Recording.ChildSpawn>>();
        foreach (Recording.ChildSpawn child in trace.Children)
        {
            if (!byType.TryGetValue(child.ChildType, out List<Recording.ChildSpawn>? group))
                byType[child.ChildType] = group = new List<Recording.ChildSpawn>();
            group.Add(child);
        }
        foreach ((int childType, List<Recording.ChildSpawn> group) in byType)
        {
            ChildModel? model = models.Find(m => m.ChildType == childType);
            if (model == null)
                models.Add(model = new ChildModel { ChildType = childType, Trigger = group[0].Trigger });
            model.Trigger = group[0].Trigger;
            group.Sort((a, b) => a.Update.CompareTo(b.Update));
            var perTick = new Dictionary<int, int>();
            int previous = -1;
            foreach (Recording.ChildSpawn child in group)
            {
                perTick[child.Update] = perTick.TryGetValue(child.Update, out int n) ? n + 1 : 1;
                if (model.Trigger == Recording.ChildTrigger.Timer && previous >= 0 && child.Update > previous)
                    model.Period.Add(child.Update - previous);
                previous = child.Update;
                model.OffsetAlong.Add(child.OffsetInParentFrame.X);
                model.OffsetAcross.Add(child.OffsetInParentFrame.Y);
                model.VelocityAlong.Add(child.VelocityInParentFrame.X);
                model.VelocityAcross.Add(child.VelocityInParentFrame.Y);
                model.DamageRatio.Add(child.DamageRatio);
            }
            foreach (int count in perTick.Values)
                model.Count.Add(count);
        }
    }

    public static void Reset()
    {
        children.Clear();
        KnowledgeRevision.Bump();
    }
}
