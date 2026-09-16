#nullable enable

using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;

namespace AICompanion.Companion.Brain.Infrastructure.WeaponKnowledge.Learning;

/// <summary>What a projectile's area damage is attached to: its death, a wall contact, or nothing seen.</summary>
public enum AreaTrigger { None, OnDeath, OnWall }

/// <summary>Area damage learned from hits whose NPC box never overlapped the projectile's box: how far it reaches, and on what event.</summary>
public readonly record struct AreaResponse(float Radius, AreaTrigger Trigger);

/// <summary>
/// One type's learned enemy response: how many distinct bodies a use's penetrate buys, the damage ratio of the
/// k-th contact hit to the first, whether repeats on one body were seen, and the area damage. Bodies per penetrate
/// is a ratio rather than a count, so a companion-modified pierce is predicted by reading the instance — declared
/// penetrate times this ratio — and nothing is relearned when mastery changes it.
/// </summary>
public sealed class HitResponse
{
    public int ProjectileType;
    public readonly Distribution PierceRatio = new();
    public float BodiesPerPenetrate => PierceRatio.Count > 0 ? PierceRatio.Median(1f) : 1f;
    public readonly List<Distribution> KthHitRatio = new();
    public bool RepeatHitsObserved;
    public AreaResponse Area = new(0f, AreaTrigger.None);
    public int Evidence;

    public float RatioForHit(int index)
        => index < KthHitRatio.Count && KthHitRatio[index].Count > 0 ? KthHitRatio[index].Median(1f) : 1f;
}

/// <summary>
/// Enemy responses from landed hits. Contact hits (boxes overlapping) teach bodies-per-penetrate over traces that
/// died with their pierce spent, per-hit damage ratios, and repeats; off-box hits teach the area radius, attached
/// to the death or wall contact they coincided with, or to the death when nothing coincided, because most area is
/// a death burst. Traces spawned under a pierce modifier teach nothing here: their bodies-per-penetrate would price
/// the modifier into the type (row K7).
/// </summary>
public static class LearnHitResponses
{
    /// <summary>How many per-hit ratios a type keeps; a use striking more bodies than this prices the rest at the last.</summary>
    public const int MaxHitsKept = 16;

    private static readonly Dictionary<int, HitResponse> responses = new();

    public static HitResponse ResponseFor(int projectileType)
    {
        if (!responses.TryGetValue(projectileType, out HitResponse? response))
            responses[projectileType] = response = new HitResponse { ProjectileType = projectileType };
        return response;
    }

    public static void Learn(Recording.FlightTrace trace)
    {
        if (trace.Modifiers.AddedPierce != 0)
            return;
        HitResponse response = ResponseFor(trace.ProjectileType);
        response.Evidence++;
        KnowledgeRevision.Bump();
        var contact = new List<Recording.BodyHit>();
        var area = new List<Recording.BodyHit>();
        foreach (Recording.BodyHit hit in trace.Hits)
            (hit.BoxOverlapsProjectile ? contact : area).Add(hit);
        LearnPierce(trace, response, contact);
        LearnRatios(response, contact);
        LearnRepeats(response, contact);
        LearnArea(trace, response, area);
    }

    private static void LearnPierce(Recording.FlightTrace trace, HitResponse response, List<Recording.BodyHit> contact)
    {
        if (trace.Death != Recording.DeathCause.PierceSpent || trace.DeclaredPenetrate <= 0 || contact.Count == 0) return;
        response.PierceRatio.Add(DistinctBodies(contact) / (float)trace.DeclaredPenetrate);
    }

    private static int DistinctBodies(List<Recording.BodyHit> contact)
    {
        var seen = new List<Rectangle>();
        foreach (Recording.BodyHit hit in contact)
        {
            bool known = false;
            foreach (Rectangle box in seen)
            {
                if (Math.Abs(box.X - hit.NpcBox.X) + Math.Abs(box.Y - hit.NpcBox.Y) < 32) { known = true; break; }
            }
            if (!known) seen.Add(hit.NpcBox);
        }
        return Math.Max(1, seen.Count);
    }

    private static void LearnRatios(HitResponse response, List<Recording.BodyHit> contact)
    {
        if (contact.Count == 0 || contact[0].Damage <= 0) return;
        for (int i = 0; i < Math.Min(contact.Count, MaxHitsKept); i++)
        {
            while (response.KthHitRatio.Count <= i)
                response.KthHitRatio.Add(new Distribution());
            response.KthHitRatio[i].Add(contact[i].Damage / (float)contact[0].Damage);
        }
    }

    private static void LearnRepeats(HitResponse response, List<Recording.BodyHit> contact)
    {
        for (int i = 0; i < contact.Count; i++)
            for (int j = i + 1; j < contact.Count; j++)
            {
                if (contact[i].NpcType != contact[j].NpcType) continue;
                if (Math.Abs(contact[i].NpcBox.X - contact[j].NpcBox.X) + Math.Abs(contact[i].NpcBox.Y - contact[j].NpcBox.Y) < 32)
                    response.RepeatHitsObserved = true;
            }
    }

    private static void LearnArea(Recording.FlightTrace trace, HitResponse response, List<Recording.BodyHit> area)
    {
        foreach (Recording.BodyHit hit in area)
        {
            float reach = DistanceToBox(trace.ProjectileType, hit);
            AreaTrigger trigger = AreaTrigger.OnDeath;
            foreach (Recording.WallContact wall in trace.Walls)
            {
                if (wall.Update == hit.Update) { trigger = AreaTrigger.OnWall; break; }
            }
            if (reach > response.Area.Radius)
                response.Area = new AreaResponse(reach, trigger);
        }
    }

    private static float DistanceToBox(int projectileType, Recording.BodyHit hit)
    {
        float dx = MathF.Max(hit.NpcBox.Left - hit.ProjectileCentre.X, MathF.Max(0f, hit.ProjectileCentre.X - hit.NpcBox.Right));
        float dy = MathF.Max(hit.NpcBox.Top - hit.ProjectileCentre.Y, MathF.Max(0f, hit.ProjectileCentre.Y - hit.NpcBox.Bottom));
        return MathF.Sqrt(dx * dx + dy * dy);
    }

    public static void Reset()
    {
        responses.Clear();
        KnowledgeRevision.Bump();
    }
}
