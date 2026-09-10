#nullable enable
using System;
using Microsoft.Xna.Framework;
using Terraria;
using AICompanion.Companion.Brain.BehaviourSelection;
using AICompanion.Companion.Brain.SharedMovementSystem;
using AICompanion.Companion.Brain.WorldObservation;
using AICompanion.Companion.Brain.BehaviourDiagnostics;

namespace AICompanion.Companion.Brain.CombatReflexes;

/// <summary>
/// Identifies imminent collisions and supplies their predicted occupied space. Locomotion
/// evaluates and performs avoidance using its own body/ability rules; no motor writes live here.
/// </summary>
public sealed class Reflexes
{
    public string? Active { get; private set; }

    public bool TryAssess(NPC npc, WorldObservation.Senses senses, BodyState live,
        out Func<BodyState, int, bool> unsafeAtTick)
    {
        bool Unsafe(BodyState state, int tick)
        {
            if (tick > Weights.DodgeLookaheadTicks) return false;
            Rectangle body = new((int)MathF.Floor(state.Left), (int)MathF.Floor(state.Bottom - BodyPhysics.Height),
                BodyPhysics.Width + 1, BodyPhysics.Height + 1);
            foreach (ThreatRecord threat in senses.Threats.Threats)
                if (threat.Npc.damage > 0 && threat.PredictedHitbox(tick).Intersects(body)) return true;
            foreach (var shot in senses.Projectiles.Threats)
                if (shot.Predict(tick).Intersects(body)) return true;
            return false;
        }
        unsafeAtTick = Unsafe;
        Active = null;
        if (senses.Threats.Threats.Count == 0 && senses.Projectiles.Threats.Count == 0) return false;
        // Ask the shared body for the passive trajectory: braking still has momentum and a
        // falling body still falls. Treating every threatened body as stationary misses both.
        BodyState predicted = live;
        for (int tick = 1; tick <= Weights.DodgeLookaheadTicks; tick++)
        {
            predicted = BodyMotion.Step(MovementQueries.World, predicted, Controls.None);
            if (!Unsafe(predicted, tick)) continue;
            BrainInspectorSamples.RecordReflex(Main.GameUpdateCount, tick, predicted);
            Active = "avoid-collision";
            return true;
        }
        return false;
    }
}
