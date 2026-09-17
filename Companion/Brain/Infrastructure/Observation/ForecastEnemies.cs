#nullable enable

using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;

namespace AICompanion.Companion.Brain.Infrastructure.Observation;

/// <summary>
/// One hostile as the combat simulator sees it: the snapshot the threat sense took this tick plus the predicted
/// boxes the observed-motion sense draws from it. Bosses and shooters are flagged because the planner prices them
/// apart; the buffs, AI state and facing are snapshot so a use simulated against this forecast reads the enemy as
/// it was when the decision started, not as the live body drifts mid-decision. Valid within the tick it was built
/// for — the predicted boxes extend it to 180 ticks out, the live scalar snapshot does not extend past the tick.
/// </summary>
public sealed class EnemyForecast
{
    public int Slot;
    public int NpcType;
    public bool IsBoss;
    public bool Shoots;
    public float Life;
    public float MaxLife;
    public int Defense;
    public float KnockbackResist;
    public bool NoGravity;
    public bool OnFire2;
    public Rectangle Box;
    public int[] BuffTypes = Array.Empty<int>();
    public readonly float[] Ai = new float[NPC.maxAI];
    public readonly float[] LocalAi = new float[NPC.maxAI];
    public int Direction;
    public Vector2 Velocity;

    private NPC? npc;

    /// <summary>The predicted box this many ticks out, by the threat sense's own method.</summary>
    public Rectangle PredictedBoxAtTick(int ticks)
    {
        if (npc == null) return Box;
        Rectangle box = Box;
        Vector2 lead = PredictObservedMotion.Predict(npc, ticks) - npc.Center;
        box.Offset((int)lead.X, (int)lead.Y);
        return box;
    }

    public Vector2 PredictedCentre(int ticks)
    {
        Rectangle box = PredictedBoxAtTick(ticks);
        return box.Center.ToVector2();
    }

    /// <summary>The observed-motion confidence this many ticks out: measured continuation, not a claim to know the next AI choice.</summary>
    public float ConfidenceAtTick(int ticks) => npc == null ? 0.5f : PredictObservedMotion.Confidence(npc, ticks);

    internal void Track(NPC live) => npc = live;

    /// <summary>
    /// The same forecast with rolled life: the beam's next segment aims and simulates against what an
    /// earlier segment leaves alive, while predictions, buffs and AI state stay the tick's snapshot.
    /// Positions stay too — pushes displacing later predictions is the one rolled term not carried —
    /// so a later segment never invents a dodge its own shots did not prove.
    /// </summary>
    public EnemyForecast RolledCopy(float life)
    {
        var copy = new EnemyForecast
        {
            Slot = Slot,
            NpcType = NpcType,
            IsBoss = IsBoss,
            Shoots = Shoots,
            Life = Math.Max(0f, life),
            MaxLife = MaxLife,
            Defense = Defense,
            KnockbackResist = KnockbackResist,
            NoGravity = NoGravity,
            OnFire2 = OnFire2,
            Box = Box,
            BuffTypes = BuffTypes,
            Direction = Direction,
            Velocity = Velocity,
        };
        Array.Copy(Ai, copy.Ai, Math.Min(Ai.Length, copy.Ai.Length));
        Array.Copy(LocalAi, copy.LocalAi, Math.Min(LocalAi.Length, copy.LocalAi.Length));
        copy.npc = npc;
        return copy;
    }
}

/// <summary>
/// The simulator's enemies: one forecast per listed hostile, built once per decision. The simulator reads these
/// and only these, so every aim of every weapon in one decision meets the same enemies; nothing in the list moves
/// while the decision runs, because the live bodies are only ever read through the tick's snapshot.
/// </summary>
public static class ForecastEnemies
{
    /// <summary>One forecast for one body outside the threat list — a stand proved against a calm target — built the same way.</summary>
    public static EnemyForecast ForSingle(NPC npc)
    {
        var forecast = new EnemyForecast
        {
            Slot = npc.whoAmI,
            NpcType = npc.type,
            Life = npc.life,
            MaxLife = npc.lifeMax,
            Defense = npc.defense,
            KnockbackResist = npc.knockBackResist,
            NoGravity = npc.noGravity,
            OnFire2 = npc.onFire2,
            Box = npc.Hitbox,
            Direction = npc.direction,
            Velocity = npc.velocity,
        };
        var buffs = new List<int>();
        for (int i = 0; i < npc.buffType.Length && i < npc.buffTime.Length; i++)
            if (npc.buffTime[i] > 0) buffs.Add(npc.buffType[i]);
        forecast.BuffTypes = buffs.ToArray();
        Array.Copy(npc.ai, forecast.Ai, Math.Min(npc.ai.Length, forecast.Ai.Length));
        Array.Copy(npc.localAI, forecast.LocalAi, Math.Min(npc.localAI.Length, forecast.LocalAi.Length));
        forecast.Track(npc);
        return forecast;
    }

    public static IReadOnlyList<EnemyForecast> FromThreats(IReadOnlyList<ThreatRecord> threats)
    {
        var forecasts = new List<EnemyForecast>(threats.Count);
        foreach (ThreatRecord threat in threats)
        {
            NPC npc = threat.Npc;
            if (npc == null || !npc.active || npc.life <= 0 || !npc.CanBeChasedBy()) continue;
            var forecast = new EnemyForecast
            {
                Slot = npc.whoAmI,
                NpcType = npc.type,
                IsBoss = threat.IsBoss,
                Shoots = threat.Shoots,
                Life = npc.life,
                MaxLife = npc.lifeMax,
                Defense = npc.defense,
                KnockbackResist = npc.knockBackResist,
                NoGravity = npc.noGravity,
                OnFire2 = npc.onFire2,
                Box = npc.Hitbox,
                Direction = npc.direction,
                Velocity = npc.velocity,
            };
            var buffs = new List<int>();
            for (int i = 0; i < npc.buffType.Length && i < npc.buffTime.Length; i++)
                if (npc.buffTime[i] > 0) buffs.Add(npc.buffType[i]);
            forecast.BuffTypes = buffs.ToArray();
            Array.Copy(npc.ai, forecast.Ai, Math.Min(npc.ai.Length, forecast.Ai.Length));
            Array.Copy(npc.localAI, forecast.LocalAi, Math.Min(npc.localAI.Length, forecast.LocalAi.Length));
            forecast.Track(npc);
            forecasts.Add(forecast);
        }
        return forecasts;
    }
}
