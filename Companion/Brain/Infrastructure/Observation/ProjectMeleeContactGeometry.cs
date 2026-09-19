using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using AICompanion.Companion.Brain.Infrastructure.Selection.Computation;
using AICompanion.Companion.Brain.Infrastructure.Selection.Courses;
using AICompanion.Companion.Brain.Infrastructure.Selection.Opportunities;

namespace AICompanion.Companion.Brain.Infrastructure.Observation;

/// <summary>Combines frozen motion, victim trajectory and vanilla melee mechanics. Native
/// defence is captured once; no hypothetical sample re-reads either living actor.</summary>
public sealed class ProjectMeleeContactGeometry
{
    private readonly CapturedMeleeEnemy enemy;
    private readonly CapturedEnemyCourseMotion motion;
    private readonly ContactBox[] victims;
    private readonly EstimateEffectiveDamage.Captured defence;
    private readonly int rawDamage, baseChannel, ordinaryReady;
    private readonly int[] channelReady;
    private readonly List<ContactSample> samples = new();
    private bool supported;
    private ContactGeometry? result;

    public ProjectMeleeContactGeometry(CapturedMeleeEnemy enemy, CapturedEnemyCourseMotion motion,
        IReadOnlyList<ContactBox> victims, EstimateEffectiveDamage.Captured defence, int rawDamage,
        int baseChannel, int ordinaryReady, IReadOnlyList<int> channelReady, bool supported)
    {
        if (enemy.Type != motion.Type || enemy.Width != motion.Width || enemy.Height != motion.Height
            || motion.CoveredTicks + 1 != motion.Centres.Length)
            throw new ArgumentException("Melee geometry and motion must describe the same captured enemy.");
        this.enemy = enemy; this.motion = motion with { Centres = motion.Centres.ToArray() };
        this.victims = victims.ToArray(); this.defence = defence; this.rawDamage = rawDamage;
        this.baseChannel = baseChannel; this.ordinaryReady = ordinaryReady;
        this.channelReady = channelReady.ToArray(); this.supported = supported;
    }

    public ContactGeometry? Continue(DecisionWorkBudget budget)
    {
        if (result != null) return result;
        int count = Math.Min(victims.Length, motion.Centres.Length);
        while (samples.Count < count)
        {
            if (!budget.TrySpend("course-melee-geometry")) return null;
            int tick = samples.Count;
            var centre = motion.Centres[tick];
            var at = enemy with { Position = new CoursePoint((float)centre.X - enemy.Width * .5f, (float)centre.Y - enemy.Height * .5f) };
            var victim = victims[tick];
            var shape = ResolveCapturedMeleeShape.Resolve(at,
                new Rectangle((int)victim.X, (int)victim.Y, (int)victim.Width, (int)victim.Height), baseChannel);
            int ready = ordinaryReady;
            if (defence.IsPlayer && shape.HitChannel >= 0)
            {
                if (shape.HitChannel >= channelReady.Length) supported = false;
                // Player.Update_NPCCollision checks ordinary immunity before melee
                // geometry can select a different channel. A native base channel
                // bypasses that first check; a channel selected by geometry does not.
                else ready = Math.Max(baseChannel == -1 ? ordinaryReady : 0, channelReady[shape.HitChannel]);
            }
            // Player contact applies this native multiplier before DamageVar rounds;
            // NPC.BeHurtByOtherNPC uses the attacker's damage without that multiplier.
            int damage = (int)Math.Round(rawDamage * (defence.IsPlayer ? shape.DamageMultiplier : 1f));
            samples.Add(new(new(shape.Box.X, shape.Box.Y, shape.Box.Width, shape.Box.Height), defence.At(damage), ready));
        }
        return result = new(Array.AsReadOnly(samples.ToArray()), supported);
    }
}
