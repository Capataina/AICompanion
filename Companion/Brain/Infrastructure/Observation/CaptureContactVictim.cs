using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Terraria;
using AICompanion.Companion.Brain.Infrastructure.Selection.Computation;
using AICompanion.Companion.Brain.Infrastructure.Selection.Courses;
using AICompanion.Companion.Brain.Infrastructure.Selection.Opportunities;

namespace AICompanion.Companion.Brain.Infrastructure.Observation;

public sealed record CapturedContactVictim(ulong Tick, HarmActor Actor, int NativeType, double Life,
    CoursePoint Position, int Width, int Height, EstimateEffectiveDamage.Captured Defence,
    bool ContactEnabled, int OrdinaryReadyTick, IReadOnlyList<int> ChannelReadyTicks,
    IReadOnlyList<bool> NoAggroTypes)
{
    public static FactKey Key(HarmActor actor) => new("contact-victim", actor.ToString());
    public DecisionFact ToFact(long revision) => new(Key(Actor), revision,
        new(Text: JsonSerializer.Serialize(this)), FactEvidence.Observed);
}

/// <summary>Native victim inputs at observation time. This captures immunity and body
/// eligibility; per-enemy rules, hit hooks and future changes remain separate evidence.</summary>
public static class CaptureContactVictim
{
    public static CapturedContactVictim? Capture(Player player, DecisionWorkBudget budget)
    {
        if (!budget.TrySpend("capture-player-contact", 1L + player.hurtCooldowns.Length + player.npcTypeNoAggro.Length)) return null;
        return new(Main.GameUpdateCount, HarmActor.Player, 0, player.dead ? 0 : Math.Max(0, player.statLife),
            new(player.position.X, player.position.Y), player.width, player.height, EstimateEffectiveDamage.Capture(player),
            !player.dead && !player.creativeGodMode,
            player.immune ? Math.Max(1, player.immuneTime) : 0,
            Array.AsReadOnly(player.hurtCooldowns.Select(value => Math.Max(0, value)).ToArray()),
            Array.AsReadOnly(player.npcTypeNoAggro.ToArray()));
    }

    public static CapturedContactVictim? Capture(NPC npc, DecisionWorkBudget budget)
    {
        if (!budget.TrySpend("capture-companion-contact")) return null;
        return new(Main.GameUpdateCount, HarmActor.Companion, npc.type, npc.active ? Math.Max(0, npc.life) : 0,
            new(npc.position.X, npc.position.Y), npc.width, npc.height, EstimateEffectiveDamage.Capture(npc),
            npc.active && !npc.dontTakeDamage && !npc.dontTakeDamageFromHostiles && !npc.immortal,
            Math.Max(0, npc.immune[255]), Array.Empty<int>(), Array.Empty<bool>());
    }
}
