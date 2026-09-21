using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Terraria;
using AICompanion.Companion.Brain.Infrastructure.Selection.Computation;
using AICompanion.Companion.Brain.Infrastructure.Selection.Courses;
using AICompanion.Companion.Brain.Infrastructure.Selection.Opportunities;

namespace AICompanion.Companion.Brain.Infrastructure.Observation;

/// <summary>
/// <paramref name="OrdinaryReadyTick"/> is how long this body's *current* immunity still has to run, and
/// <paramref name="ImmunityTicks"/> is how long a fresh hit would grant. They are different questions and
/// the forecast needs both: the first says when the next hit can land at all, the second says how often
/// hits can land after that, and a forecast holding only the first can price one hit and never a cadence.
/// </summary>
public sealed record CapturedContactVictim(ulong Tick, HarmActor Actor, int NativeType, double Life,
    CoursePoint Position, int Width, int Height, EstimateEffectiveDamage.Captured Defence,
    bool ContactEnabled, int OrdinaryReadyTick, int ImmunityTicks, int MinimalHitImmunityTicks,
    IReadOnlyList<int> ChannelReadyTicks)
{
    public static FactKey Key(HarmActor actor) => new("contact-victim", actor.ToString());
    public DecisionFact ToFact(long revision) => new(Key(Actor), revision,
        new(Text: JsonSerializer.Serialize(this)), FactEvidence.Observed);
}

/// <summary>Native victim inputs at observation time. This captures immunity and body
/// eligibility; per-enemy rules, hit hooks and future changes remain separate evidence.</summary>
public static class CaptureContactVictim
{
    /// <summary>
    /// The player as a contact victim. The no-aggro table is deliberately not captured: it is sized to
    /// the whole NPC type table, so copying it charged several hundred operations of the shared planning
    /// allowance on every observation — which is why this capture was taken out of the snapshot
    /// entirely — and it had no reader anywhere in the tree. Contact damage in Terraria does not consult
    /// aggro at all; a hostile that ignores the player still hurts him by walking into him, so nothing
    /// the harm forecast asks needs it. If targeting ever needs the table it comes back as its own fact,
    /// captured by whoever reads it and charged to them.
    /// </summary>
    public static CapturedContactVictim? Capture(Player player, DecisionWorkBudget budget)
    {
        if (!budget.TrySpend("capture-player-contact", 1L + player.hurtCooldowns.Length)) return null;
        return new(Main.GameUpdateCount, HarmActor.Player, 0, player.dead ? 0 : Math.Max(0, player.statLife),
            new(player.position.X, player.position.Y), player.width, player.height, EstimateEffectiveDamage.Capture(player),
            !player.dead && !player.creativeGodMode,
            player.immune ? Math.Max(1, player.immuneTime) : 0,
            // `Player.Hurt`'s own arithmetic for the ordinary contact channel, read from the decompiled
            // source rather than remembered: `immuneTime = pvP ? 8 : (damage == 1 ? (longInvince ? 40 :
            // 20) : (longInvince ? 80 : 40))`. This is singleplayer, so the pvp arm cannot be reached.
            // A special hit channel selected by the melee geometry takes `hurtCooldowns[channel]` instead
            // of `immuneTime`, and the two lines set the same numbers, so one pair covers both routes.
            // `longInvince` is the Cross Necklace and its family; reading the flag rather than the
            // constant is what makes an accessory change the cadence without anything here knowing why.
            player.longInvince ? 80 : 40,
            player.longInvince ? 40 : 20,
            Array.AsReadOnly(player.hurtCooldowns.Select(value => Math.Max(0, value)).ToArray()));
    }

    public static CapturedContactVictim? Capture(NPC npc, DecisionWorkBudget budget)
    {
        if (!budget.TrySpend("capture-companion-contact")) return null;
        return new(Main.GameUpdateCount, HarmActor.Companion, npc.type, npc.active ? Math.Max(0, npc.life) : 0,
            new(npc.position.X, npc.position.Y), npc.width, npc.height, EstimateEffectiveDamage.Capture(npc),
            npc.active && !npc.dontTakeDamage && !npc.dontTakeDamageFromHostiles && !npc.immortal,
            Math.Max(0, npc.immune[255]),
            // `NPC.BeHurtByOtherNPC` sets `immune[255] = 30`, or 20 for type 548. There is no
            // damage-sized branch on this side, so the minimal-hit window is the same number: the two
            // fields keep one shape across both kinds of body rather than one of them carrying a null
            // that every reader would have to decide what to do with.
            npc.type == 548 ? 20 : 30, npc.type == 548 ? 20 : 30, Array.Empty<int>());
    }
}
