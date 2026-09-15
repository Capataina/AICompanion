#nullable enable
using System;
using Microsoft.Xna.Framework;
using Terraria;
using AICompanion.Companion.Brain.Infrastructure.Selection;
using AICompanion.Companion.Brain.Infrastructure.Movement;
using AICompanion.Companion.Brain.Infrastructure.Observation;
using AICompanion.Companion.Brain.Infrastructure.Diagnostics;

namespace AICompanion.Companion.Brain.SharedBehaviours.Safety;

/// <summary>
/// Identifies imminent collisions and supplies their predicted occupied space. Movement
/// evaluates and performs avoidance through the orb's own contact; no motor writes live here.
/// </summary>
public sealed class Reflexes
{
    public string? Active { get; private set; }

    /// <summary>The engine's box for the orb at <paramref name="centre"/>, grown by a pixel each way so a hitbox that
    /// merely touches the circle's bounding square still counts: the game tests overlap on integer boxes.</summary>
    public static Rectangle BodyAt(Vector2 centre)
        => new((int)MathF.Floor(centre.X - CircleContact.Radius), (int)MathF.Floor(centre.Y - CircleContact.Radius),
            (int)CircleContact.Diameter + 1, (int)CircleContact.Diameter + 1);

    public bool TryAssess(NPC npc, Infrastructure.Observation.Senses senses, OrbState live,
        out Func<OrbState, int, bool> unsafeAtTick)
    {
        bool Unsafe(OrbState state, int tick)
        {
            if (tick > Weights.DodgeLookaheadTicks) return false;
            Rectangle body = BodyAt(state.Centre);
            foreach (ThreatRecord threat in senses.Threats.Threats)
                if (threat.Npc.damage > 0 && threat.PredictedHitbox(tick).Intersects(body)) return true;
            foreach (var shot in senses.Projectiles.Threats)
                if (shot.Predict(tick).Intersects(body)) return true;
            return false;
        }
        unsafeAtTick = Unsafe;
        Active = null;
        if (senses.Threats.Threats.Count == 0 && senses.Projectiles.Threats.Count == 0) return false;
        // The passive trajectory: an orb carries its momentum and nothing pulls it down, so the body
        // it will be in a few ticks from now is this one coasting, stopped only by the walls it meets.
        ITileWorld world = MovementQueries.World;
        Vector2 centre = live.Centre, velocity = live.Velocity;
        for (int tick = 1; tick <= Weights.DodgeLookaheadTicks; tick++)
        {
            centre += velocity;
            CircleContact.Resolve(world, ref centre, ref velocity);
            OrbState predicted = live with { Centre = centre, Velocity = velocity };
            if (!Unsafe(predicted, tick)) continue;
            BrainInspectorSamples.RecordReflex(Main.GameUpdateCount, tick, predicted);
            Active = "avoid-collision";
            return true;
        }
        return false;
    }
}
