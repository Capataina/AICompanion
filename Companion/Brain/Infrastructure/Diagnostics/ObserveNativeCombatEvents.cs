#nullable enable

using Microsoft.Xna.Framework;
using Terraria;
using Terraria.DataStructures;
using Terraria.ModLoader;

namespace AICompanion.Companion.Brain.Infrastructure.Diagnostics;

/// <summary>A new world drop gets a new identity even when Terraria reuses its item slot.</summary>
public sealed class ObserveNativeItemEvents : GlobalItem
{
    public override void OnSpawn(Item item, IEntitySource source) => GodsEyeEvents.RecordItemSpawn(item);
}

/// <summary>Native outcomes the brain cannot infer: spawn generations, projectile contact, and death.</summary>
public sealed class ObserveNativeNpcEvents : GlobalNPC
{
    public override void OnSpawn(NPC npc, IEntitySource source) { global::AICompanion.Companion.Brain.Infrastructure.Observation.PredictObservedMotion.Forget(npc.whoAmI); GodsEyeEvents.RecordNpcSpawn(npc); }
    public override void OnKill(NPC npc) { GodsEyeEvents.RecordNpcDeath(npc); global::AICompanion.Companion.Brain.Infrastructure.Observation.PredictObservedMotion.Forget(npc.whoAmI); }
    public override void ModifyHitByProjectile(NPC npc, Projectile projectile, ref NPC.HitModifiers modifiers)
    {
        var attribution = global::AICompanion.Companion.Brain.Infrastructure.Interactions.Firing.TrackLandedHits.IsCompanionShot(projectile.whoAmI)
            ? global::AICompanion.Companion.Brain.Infrastructure.Observation.NativeEffectAttribution.CompanionProjectile
            : global::AICompanion.Companion.Brain.Infrastructure.Observation.NativeEffectAttribution.Unknown;
        global::AICompanion.Companion.Brain.Infrastructure.Observation.CollectNativeEffectReceipts.BeginProjectileStrike(npc, projectile, attribution);
    }
    public override void OnHitByProjectile(NPC npc, Projectile projectile, NPC.HitInfo hit, int damageDone)
    {
        global::AICompanion.Companion.Brain.Infrastructure.Observation.CollectNativeEffectReceipts.CompleteProjectileStrike(npc, projectile, damageDone,
            "GlobalNPC.OnHitByProjectile");
        GodsEyeEvents.RecordProjectileOutcome(projectile, npc, "enemy-hit");
        GodsEyeEvents.RecordEffectiveNpcDamage(npc, hit, damageDone);
    }
}

/// <summary>
/// Decompilation shows Player.StrikeNPCDirect calling PlayerLoader.OnHitNPC after the native strike;
/// this hook is therefore the one owner for item, dash, mount and other player-to-NPC physical strikes.
/// It does not attribute a player strike to the companion.
/// </summary>
public sealed class ObserveNativePlayerCombatEvents : ModPlayer
{
    public override void ModifyHitNPC(NPC target, ref NPC.HitModifiers modifiers)
        => global::AICompanion.Companion.Brain.Infrastructure.Observation.CollectNativeEffectReceipts.BeginPlayerStrike(target);

    public override void OnHitNPC(NPC target, NPC.HitInfo hit, int damageDone)
        => global::AICompanion.Companion.Brain.Infrastructure.Observation.CollectNativeEffectReceipts.CompletePlayerStrike(target, damageDone,
            "ModPlayer.OnHitNPC");
}

/// <summary>Records terrain contact and terminal disappearance as distinct outcomes, never as inferred kills.</summary>
public sealed class ObserveNativeProjectileEvents : GlobalProjectile
{
    public override void OnSpawn(Projectile projectile, IEntitySource source)
        => GodsEyeEvents.RecordProjectileOutcome(projectile, null, "spawn");

    public override bool OnTileCollide(Projectile projectile, Vector2 oldVelocity)
    {
        GodsEyeEvents.RecordProjectileOutcome(projectile, null, "terrain-hit");
        return true;
    }

    public override void OnKill(Projectile projectile, int timeLeft)
        => GodsEyeEvents.RecordProjectileOutcome(projectile, null, "ended");
}
