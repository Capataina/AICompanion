#nullable enable

extern alias live;

using System;
using System.Collections.Generic;
using System.Globalization;
using AICompanion.Tools.SessionReport;
using live::AICompanion.Companion.Brain.Infrastructure.WeaponKnowledge;

namespace AICompanion.Tools.CombatAudit;

/// <summary>
/// The knowledge audit: every shot's predicted hits against what the world recorded afterwards.
/// Predicted bodies arrive as live slots, landed damage as stable identities, so the npc-spawn and
/// npc-death records join them across the capture; a prediction matches a landed hit on the same body
/// within ten ticks and a quarter of the predicted damage. Per projectile type the audit reports the
/// pairing rate, the tick and damage error, the wall-contact surprise, and the residual learner's
/// factor at the shot's tick from the telemetry beside the capture.
/// </summary>
internal static class AuditKnowledge
{
    public sealed record TypeCalibration(string Type, int Shots, int Paired, int PredictedHits, int MatchedHits,
        double DamagePredicted, double DamageLanded, double MeanTickError, int WallSurprise, double MeanFactor);

    public sealed record KnowledgeVerdict(List<TypeCalibration> Types)
    {
        public double MissRate(string type)
        {
            foreach (TypeCalibration row in Types)
                if (row.Type == type)
                    return row.PredictedHits == 0 ? 0.0 : 1.0 - (double)row.MatchedHits / row.PredictedHits;
            return 0.0;
        }
    }

    public static KnowledgeVerdict Audit(IReadOnlyList<GodsEyeEvent> events, Session? session)
    {
        var slotTimeline = new Dictionary<int, List<(long From, long To, int Stable, int Type)>>();
        foreach (GodsEyeEvent e in events)
        {
            if (e.kind != "npc-spawn" && e.kind != "npc-death")
                continue;
            if (e.Field("slot") is not { } slotText || !int.TryParse(slotText, out int slot))
                continue;
            if (!slotTimeline.TryGetValue(slot, out var spans))
                slotTimeline[slot] = spans = new List<(long, long, int, int)>();
            if (e.kind == "npc-spawn" && int.TryParse(e.related, out int type))
                spans.Add((e.tick, long.MaxValue, e.subject, type));
            else if (e.kind == "npc-death")
            {
                for (int i = spans.Count - 1; i >= 0; i--)
                    if (spans[i].Stable == e.subject && spans[i].To == long.MaxValue)
                        spans[i] = (spans[i].From, e.tick, spans[i].Stable, spans[i].Type);
            }
        }
        int StableAt(int slot, long tick, out int type)
        {
            type = 0;
            if (!slotTimeline.TryGetValue(slot, out var spans))
                return -1;
            foreach ((long from, long to, int stable, int spanType) in spans)
                if (tick >= from && tick <= to)
                {
                    type = spanType;
                    return stable;
                }
            return -1;
        }
        var damage = new List<(int Stable, long Tick, float Amount)>();
        foreach (GodsEyeEvent e in events)
            if (e.kind == "npc-damage")
                damage.Add((e.subject, e.tick, e.amount));
        var shotEvents = new Dictionary<int, GodsEyeEvent>();
        foreach (GodsEyeEvent e in events)
            if (e.kind == "shot-event")
                shotEvents[e.subject] = e;
        var identity = new WeaponIdentity();
        var rows = new Dictionary<string, Accumulator>();
        foreach (GodsEyeEvent shot in events)
        {
            if (shot.kind != "shot")
                continue;
            if (shot.ChannelField("projectile") is not { } link || !int.TryParse(link, out int projectile))
                continue;
            if (!shotEvents.TryGetValue(projectile, out GodsEyeEvent? landed))
                continue;
            string type = identity.NameOfProjectile(ProjectileTypeOf(landed));
            if (!rows.TryGetValue(type, out Accumulator? row))
                rows[type] = row = new Accumulator();
            row.Shots++;
            row.Paired++;
            bool predictedBody = false;
            foreach ((int slot, int relTick, float dmg) in PredictedHits(shot))
            {
                predictedBody = true;
                row.PredictedHits++;
                row.DamagePredicted += dmg;
                int stable = StableAt(slot, shot.tick, out _);
                if (stable < 0)
                    continue;
                long expectTick = shot.tick + relTick;
                foreach ((int hitStable, long hitTick, float amount) in damage)
                {
                    if (hitStable != stable || Math.Abs(hitTick - expectTick) > 10)
                        continue;
                    if (Math.Abs(amount - dmg) > Math.Max(2f, dmg * 0.25f))
                        continue;
                    row.MatchedHits++;
                    row.DamageLanded += amount;
                    row.TickError += hitTick - expectTick;
                    break;
                }
            }
            if (predictedBody && FirstWallTick(landed) >= 0 && row.MatchedHits == row.MatchedBefore)
                row.WallSurprise++;
            row.MatchedBefore = row.MatchedHits;
            double factor = FactorAt(session, shot.tick);
            if (double.IsFinite(factor))
            {
                row.Factor += factor;
                row.Factored++;
            }
        }
        var types = new List<TypeCalibration>();
        foreach ((string type, Accumulator row) in rows)
            types.Add(new TypeCalibration(type, row.Shots, row.Paired, row.PredictedHits, row.MatchedHits,
                row.DamagePredicted, row.DamageLanded,
                row.MatchedHits == 0 ? 0.0 : row.TickError / (double)row.MatchedHits,
                row.WallSurprise, row.Factored == 0 ? double.NaN : row.Factor / row.Factored));
        types.Sort((a, b) => string.CompareOrdinal(a.Type, b.Type));
        return new KnowledgeVerdict(types);
    }

    private sealed class Accumulator
    {
        public int Shots;
        public int Paired;
        public int PredictedHits;
        public int MatchedHits;
        public int MatchedBefore;
        public double DamagePredicted;
        public double DamageLanded;
        public double TickError;
        public int WallSurprise;
        public double Factor;
        public int Factored;
    }

    private static IEnumerable<(int Slot, int Tick, float Damage)> PredictedHits(GodsEyeEvent shot)
    {
        if (shot.Field("predicted") is not { } predicted || predicted.Length == 0)
            yield break;
        foreach (string part in predicted.Split('+'))
        {
            int at = part.IndexOf('@'), colon = part.IndexOf(':');
            if (at < 0 || colon < 0)
                continue;
            if (int.TryParse(part[..at], out int slot) && int.TryParse(part[(at + 1)..colon], out int tick)
                && float.TryParse(part[(colon + 1)..], NumberStyles.Float, CultureInfo.InvariantCulture, out float damage))
                yield return (slot, tick, damage);
        }
    }

    private static int ProjectileTypeOf(GodsEyeEvent landed)
        => int.TryParse(landed.label, out int type) ? type : 0;

    /// <summary>The first wall contact's tick, or -1. Parsed by hand because the value nests like
    /// the first hit's: <c>first-wall=tick=..;..</c> sits inside the semicolon detail.</summary>
    private static long FirstWallTick(GodsEyeEvent landed)
    {
        int start = landed.detail.IndexOf("first-wall=", StringComparison.Ordinal);
        if (start < 0)
            return -1;
        int tick = landed.detail.IndexOf("tick=", start, StringComparison.Ordinal);
        if (tick < 0)
            return -1;
        tick += "tick=".Length;
        int end = tick;
        while (end < landed.detail.Length && (char.IsDigit(landed.detail[end]) || landed.detail[end] == '-'))
            end++;
        return long.TryParse(landed.detail[tick..end], out long value) ? value : -1;
    }

    private static double FactorAt(Session? session, long tick)
    {
        if (session == null)
            return double.NaN;
        Column? ticks = session.Find("tick"), residuals = session.Find("knowledge_residual");
        if (ticks == null || residuals == null)
            return double.NaN;
        for (int i = 0; i < ticks.Number.Length && i < residuals.Text.Length; i++)
        {
            if ((long)ticks.Number[i] != tick)
                continue;
            string[] parts = residuals.Text[i].Split(',');
            if (parts.Length >= 1 && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double sampled))
                return sampled;
        }
        return double.NaN;
    }
}
