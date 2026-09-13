#nullable enable

using System;
using Terraria;

namespace AICompanion.Companion.Brain.Infrastructure.Observation;

/// <summary>
/// What one hit takes off a body, by the game's own arithmetic rather than the attacker's raw number.
///
/// The player's side builds the modifiers Player.Hurt builds for an ordinary hit — endurance folded
/// into FinalDamage as ApplyVanillaHurtEffectModifiers does — and calls
/// <see cref="Player.HurtModifiers.GetDamage"/> with the player's own defence and
/// <see cref="Player.DefenseEffectiveness"/>, which the game sets in ResetEffects to a half in Classic,
/// three quarters in Expert and all of it in Master. The companion is an NPC struck through
/// NPC.BeHurtByOtherNPC, so its side builds what NPC.GetIncomingStrikeModifiers builds — defence at the
/// NPC's fixed half effectiveness, negative defence as bonus damage, ichor and Betsy's curse, the taken
/// damage multiplier and super armour — and calls <see cref="NPC.HitModifiers.GetDamage"/>. The same
/// hit therefore costs the companion more than an Expert or Master player wearing the same armour,
/// because the companion copies the player's defence but not the player's effectiveness; that is the
/// game's rule, reproduced rather than repaired.
///
/// Neither side runs the loader's ModifyHitByNPC or ModifyIncomingHit hooks. Those exist for real hits
/// and a mod may spend a shield charge or play a sound inside one, so calling them for a hypothetical
/// hit on every tick would run someone else's side effects; modded resistances are not reflected.
/// Damage variation is left out too, so this is the median hit: the game varies contact damage by up
/// to fifteen percent before defence, which matters most near the one-damage floor.
/// </summary>
public static class EstimateEffectiveDamage
{
    public static float ToPlayer(Player player, int rawDamage)
    {
        if (rawDamage <= 0) return 0f;
        var modifiers = new Player.HurtModifiers();
        modifiers.FinalDamage *= MathF.Max(1f - player.endurance, 0f);
        return modifiers.GetDamage(rawDamage, player.statDefense, player.DefenseEffectiveness.Value);
    }

    public static float ToNpc(NPC npc, int rawDamage)
    {
        if (rawDamage <= 0) return 0f;
        var modifiers = new NPC.HitModifiers { SuperArmor = npc.SuperArmor };
        modifiers.FinalDamage *= npc.takenDamageMultiplier;
        if (npc.defense >= 0) modifiers.Defense.Base += npc.defense;
        else modifiers.FlatBonusDamage += -npc.defense;
        if (npc.ichor) modifiers.Defense.Flat -= 15f;
        if (npc.betsysCurse) modifiers.Defense.Flat -= 40f;
        return modifiers.GetDamage(rawDamage, crit: false, damageVariation: false);
    }
}
