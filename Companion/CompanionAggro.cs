#nullable enable

using Terraria;
using Terraria.ModLoader;

namespace AICompanion.Companion;

/// <summary>
/// Makes enemies hunt the companion. Vanilla enemies choose a target by one loop over
/// the player slots, and nothing else; the companion's stand-in player sits in a slot,
/// inactive, and this makes it active for exactly the span in which a hostile's AI runs,
/// so the loop sees a second player and every boss's "is anyone still alive nearby"
/// check sees it too. Outside that span the slot is inactive and no other reader of
/// the array (the player update, spawn waves, projectile-versus-player collision,
/// biome and event bookkeeping) ever meets it. A downed companion mirrors as dead, so
/// it is not a target and not a reason to stay.
///
/// Damage still arrives through the NPC: contact damage from hostiles to friendly NPCs
/// and hostile projectiles against friendly NPCs are both the game's own rules, and
/// aiming at the stand-in puts them on the companion's hitbox.
///
/// The alternative, a per-AI-style redirect, was rejected: it would be written once per
/// vanilla AI style and never cover a modded enemy, where this covers anything that
/// calls the game's targeting.
/// </summary>
public sealed class CompanionAggro : GlobalNPC
{
    /// <summary>The kill switch: false and enemies ignore the companion as before.</summary>
    public static bool Enabled = true;

    public override bool PreAI(NPC npc)
    {
        if (Enabled && IsHostile(npc))
            CompanionBody.Expose(true);
        return true;
    }

    public override void PostAI(NPC npc)
    {
        CompanionBody.Expose(false);
    }

    public override void Unload()
    {
        CompanionBody.Withdraw();
        Enabled = true;
    }

    private static bool IsHostile(NPC npc) => !npc.friendly && !npc.townNPC && npc.damage > 0;
}
