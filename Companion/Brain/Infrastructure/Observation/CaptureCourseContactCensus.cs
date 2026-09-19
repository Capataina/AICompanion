using System;
using System.Collections.Generic;
using Terraria;
using AICompanion.Companion.Brain.Infrastructure.Selection.Computation;

namespace AICompanion.Companion.Brain.Infrastructure.Observation;

public sealed record CapturedContactEnemy(int Slot, long Generation, CapturedMeleeEnemy Shape, int Damage,
    PredictObservedMotion.ExportedTrack Motion);
public sealed record CapturedContactCensus(ulong Tick, int ExaminedSlots, int TotalSlots,
    IReadOnlyList<CapturedContactEnemy> Enemies, bool Complete);

/// <summary>Copies potential hostile contact sources in native slot order during one
/// observation. Eligibility against either victim and future AI remain separate questions.</summary>
public static class CaptureCourseContactCensus
{
    public static CapturedContactCensus Capture(DecisionWorkBudget budget)
    {
        ulong tick = Main.GameUpdateCount;
        var enemies = new List<CapturedContactEnemy>();
        int slot = 0;
        for (; slot < Main.maxNPCs; slot++)
        {
            if (!budget.TrySpend("capture-course-contact-slot")) break;
            var enemy = Main.npc[slot];
            if (enemy is not { active: true, friendly: false } || enemy.damage <= 0) continue;
            enemies.Add(CaptureEnemy(enemy, HostileAttackSources.Generation(enemy)));
        }
        // Capture is a main-thread observation, not a frontier that may resume while
        // live NPCs move. A partial census belongs only to the tick that produced it.
        if (Main.GameUpdateCount != tick)
            throw new InvalidOperationException("A contact census cannot span native observation ticks.");
        return new(tick, slot, Main.maxNPCs, enemies.AsReadOnly(), slot == Main.maxNPCs);
    }

    internal static CapturedContactEnemy CaptureEnemy(NPC enemy, long generation)
    {
        PredictObservedMotion.Observe(enemy);
        return new(enemy.whoAmI, generation, CapturedMeleeEnemy.From(enemy), enemy.damage,
            PredictObservedMotion.ExportTrack(enemy.whoAmI)!);
    }
}
