#nullable enable

using Microsoft.Xna.Framework;
using Terraria;
using Terraria.DataStructures;
using Terraria.ModLoader;

namespace AICompanion.Companion.Brain.BehaviourDiagnostics;

/// <summary>A new world drop gets a new identity even when Terraria reuses its item slot.</summary>
public sealed class ObserveNativeItemEvents : GlobalItem
{
    public override void OnSpawn(Item item, IEntitySource source) => GodsEyeEvents.RecordItemSpawn(item);
}

/// <summary>Native outcomes the brain cannot infer: spawn generations, projectile contact, and death.</summary>
public sealed class ObserveNativeNpcEvents : GlobalNPC
{
    public override void OnSpawn(NPC npc, IEntitySource source) { global::AICompanion.Companion.Brain.WorldObservation.PredictObservedMotion.Forget(npc.whoAmI); GodsEyeEvents.RecordNpcSpawn(npc); }
    public override void OnKill(NPC npc) { GodsEyeEvents.RecordNpcDeath(npc); global::AICompanion.Companion.Brain.WorldObservation.PredictObservedMotion.Forget(npc.whoAmI); }
    public override void OnHitByProjectile(NPC npc, Projectile projectile, NPC.HitInfo hit, int damageDone)
    {
        GodsEyeEvents.RecordProjectileOutcome(projectile, npc, "enemy-hit");
        GodsEyeEvents.RecordEffectiveNpcDamage(npc, hit, damageDone);
    }
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
