#nullable enable

using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;

namespace AICompanion.Companion.Brain.Infrastructure.WeaponKnowledge.Learning;

/// <summary>What a projectile type does at a wall: dies, passes through, reflects, or stops dead.</summary>
public enum WallKind { Unknown, Dies, Passes, Reflects, Stops }

/// <summary>
/// One type's learned wall response. A reflecting type carries per-axis restitution, the speed kept through a
/// bounce, how many contacts precede a wall death, and — when the last bounce departs from reflection — the
/// homing the remaining flight is flown with from that bounce on.
/// </summary>
public sealed class WallResponse
{
    public int ProjectileType;
    public WallKind Kind;
    public float RestitutionNormal = 1f;
    public float RestitutionTangent = 1f;
    public float SpeedFactorAfterBounce = 1f;
    public int BounceCount = 1;
    public bool RetargetingBounce;
    public HomingTerm? BounceHoming;
    public int Evidence;

    public static WallResponse Unknown(int projectileType) => new() { ProjectileType = projectileType, Kind = WallKind.Unknown };
}

/// <summary>
/// Wall responses from tile contacts. A type whose sample never collides passes by reading; a type never seen
/// touch a wall stays unknown rather than guessed dead, because the first contact is still ahead of it. Contacts
/// that killed every time mean dies; survivals with a flipped axis mean reflects, with restitution and kept speed
/// as medians; survivals that kept no speed mean stops. Mixed deaths and survivals are a bounce count: contacts
/// before a wall death. The simulator reflects with the game's own axis answer and these learned factors.
/// </summary>
public static class LearnWallResponses
{
    private static readonly Dictionary<int, WallResponse> responses = new();

    public static WallResponse ResponseFor(int projectileType)
    {
        if (responses.TryGetValue(projectileType, out WallResponse? response))
            return response;
        var fresh = WallResponse.Unknown(projectileType);
        if (ContentSamples.ProjectilesByType.TryGetValue(projectileType, out Projectile? sample) && sample != null && !sample.tileCollide)
            fresh.Kind = WallKind.Passes;
        return fresh;
    }

    /// <summary>Fold one closed trace's contacts into its type's response. Modified traces count: no modifier changes what a wall does.</summary>
    public static void Learn(Recording.FlightTrace trace)
    {
        if (!responses.TryGetValue(trace.ProjectileType, out WallResponse? response))
            responses[trace.ProjectileType] = response = WallResponse.Unknown(trace.ProjectileType);
        if (trace.Walls.Count == 0)
        {
            if (response.Kind == WallKind.Unknown && ResponseFor(trace.ProjectileType).Kind == WallKind.Passes)
                response.Kind = WallKind.Passes;
            return;
        }
        response.Evidence++;
        KnowledgeRevision.Bump();
        int died = 0, reflected = 0, stopped = 0;
        var normal = new List<float>();
        var tangent = new List<float>();
        var kept = new List<float>();
        foreach (Recording.WallContact contact in trace.Walls)
        {
            if (contact.Died) { died++; continue; }
            float inSpeed = contact.VelocityIn.Length();
            float outSpeed = contact.VelocityOut.Length();
            if (inSpeed <= 0f) continue;
            kept.Add(outSpeed / inSpeed);
            if (outSpeed < inSpeed * 0.05f) { stopped++; continue; }
            reflected++;
            if (contact.Normal != Vector2.Zero)
            {
                Vector2 n = contact.Normal;
                Vector2 t = new(-n.Y, n.X);
                float inN = MathF.Abs(Vector2.Dot(contact.VelocityIn, n));
                float inT = MathF.Abs(Vector2.Dot(contact.VelocityIn, t));
                if (inN > 0.01f) normal.Add(Vector2.Dot(contact.VelocityOut, n) / Vector2.Dot(contact.VelocityIn, n));
                if (inT > 0.01f) tangent.Add(Vector2.Dot(contact.VelocityOut, t) / Vector2.Dot(contact.VelocityIn, t));
            }
        }
        if (reflected == 0 && stopped == 0)
        {
            if (response.Kind == WallKind.Unknown) response.Kind = WallKind.Dies;
            return;
        }
        if (response.Kind is WallKind.Unknown or WallKind.Dies)
            response.Kind = reflected > 0 ? WallKind.Reflects : WallKind.Stops;
        if (normal.Count > 0) response.RestitutionNormal = Median(normal);
        if (tangent.Count > 0) response.RestitutionTangent = Median(tangent);
        if (kept.Count > 0) response.SpeedFactorAfterBounce = Median(kept);
        if (died > 0 && trace.Death == Recording.DeathCause.Wall)
            response.BounceCount = Math.Max(1, trace.Walls.Count - 1);
        CheckRetargeting(trace, response);
    }

    /// <summary>
    /// A retargeting bounce departs from reflection by more than the flight residual allows. The departure is flown
    /// as NPC homing switching on at that bounce, fitted from the bounces' turn toward the nearest body; with no
    /// body near any of them the flag stands and the flight continues straight, because a retarget at nothing
    /// observed is a direction no trace names. No vanilla type retargets, so no row pins this — Calamity's shotgun
    /// pellet is the case it waits on.
    /// </summary>
    private static void CheckRetargeting(Recording.FlightTrace trace, WallResponse response)
    {
        Recording.WallContact last = trace.Walls[^1];
        if (last.Died || last.VelocityIn == Vector2.Zero || last.VelocityOut == Vector2.Zero) return;
        Vector2 expect = Reflect(last.VelocityIn, last.Normal) * response.SpeedFactorAfterBounce;
        float depart = Vector2.Distance(last.VelocityOut, expect);
        float bound = Math.Max(0.5f, FitFlightLaws.LawFor(trace.ProjectileType).ResidualPerUpdate * 4f);
        if (depart <= bound || response.RetargetingBounce) return;
        response.RetargetingBounce = true;
        response.BounceHoming = new HomingTerm(last.Update, 800f, HomingAnchor.NearestNpc, 0.2f, last.VelocityOut.Length());
    }

    private static Vector2 Reflect(Vector2 velocity, Vector2 normal)
    {
        if (normal == Vector2.Zero) return -velocity;
        return velocity - 2f * Vector2.Dot(velocity, normal) * normal;
    }

    private static float Median(List<float> values)
    {
        values.Sort();
        int n = values.Count;
        return n % 2 == 1 ? values[n / 2] : (values[n / 2 - 1] + values[n / 2]) / 2f;
    }

    public static void Reset()
    {
        responses.Clear();
        KnowledgeRevision.Bump();
    }
}
