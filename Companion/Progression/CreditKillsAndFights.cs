#nullable enable

using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.ModLoader;
using AICompanion.Companion.Brain.Infrastructure.Diagnostics;

namespace AICompanion.Companion.Progression;

/// <summary>Who landed a strike: the companion's own weapon, the local player's weapon, projectile, minion or sentry, or
/// anything else — a trap, a town NPC's shot, another player's projectile.</summary>
public enum Striker { Other, Player, Companion }

/// <summary>
/// Decides which kills earn experience, who earned them, and when a boss fight is over, and hands each credit to the
/// character's <see cref="CompanionExperience"/>.
///
/// A killing blow is found the way the game finds its own (<c>NPCKillAttempt</c> in <c>Player.StrikeNPCDirect</c> and
/// <c>Projectile.Damage</c>): the target is read just before a strike and again just after, and the strike killed it when
/// the body it belongs to is no longer the same living NPC. The before half is the modify hook, which runs before
/// <c>StrikeNPC</c>; the after half is the on-hit hook, which runs after it. <c>GlobalNPC.OnKill</c> is not used for this,
/// because NPCLoot returns before it whenever a mod's PreKill refuses, and the ledger would then lose the kill silently. A
/// death with no strike around it — a debuff ticking, lava, falling — is nobody's killing blow and earns nothing.
///
/// A worm or any body sharing <c>realLife</c> is one enemy: every strike is recorded against the NPC that holds the life,
/// and <c>StrikeNPC</c> runs that NPC's <c>checkDead</c> inside the strike, so the whole worm dies inside the strike that
/// killed it and counts once at its whole life.
///
/// A boss fight is every boss body alive at once. A body is an NPC holding its own life that is flagged <c>boss</c> or
/// <c>NPCID.Sets.ShouldBeCountedAsBoss</c>, which covers the Twins, Moon Lord's core, head and hands, the Destroyer's head
/// (its segments hold no life of their own), the Eater of Worlds' heads and every modded boss that sets the flag. The
/// fight's life is the sum of its bodies' maximum life. A fight ends on the tick no body is left; it is credited once, at
/// its whole life, if any body died rather than left, full when the companion landed the last strike on the last body
/// that died and half otherwise. A boss's killing blow is its last strike rather than a strike that ended it, because a
/// boss may pass through a death state first (Moon Lord's core resets its life and turns invulnerable before it dies).
///
/// Boss parts and minions that carry no boss flag — Skeletron's hands, Prime's arms, Golem's fists, servants, probes,
/// creepers — are the fight's company rather than its bodies: an NPC in <c>NPCID.Sets.BossBestiaryPriority</c> that is not a
/// body, or one spawned by a body's or company NPC's own AI, earns nothing and sets no anchor. Counted as ordinary enemies,
/// a Prime arm's life would pin the enemy anchor for the rest of the game; counted as bodies, a boss that spawns minions
/// without end would grow its own fight's life without end.
/// </summary>
public static class CreditKillsAndFights
{
    private readonly record struct Snapshot(int Slot, int Generation, int Type, int LifeMax, Striker By, bool Counts, bool Body, bool Company, Vector2 Where);

    private static readonly int[] generations = new int[Main.maxNPCs + 1];
    private static readonly bool[] spawnedByFight = new bool[Main.maxNPCs + 1];
    // The strike in progress per target slot, taken in the modify hook and consumed by the on-hit hook of the same strike.
    private static readonly Snapshot?[] pending = new Snapshot?[Main.maxNPCs + 1];

    private sealed class Body
    {
        public int Slot, Generation, LifeMax;
        public Striker LastStriker;
        public bool Died;
        public ulong LeftTick;
    }

    private static readonly Dictionary<long, Body> fight = new();
    private static HashSet<int>? bestiaryCompany;

    /// <summary>How many boss bodies the open fight has seen; zero with no fight.</summary>
    public static int FightBodies => fight.Count;

    public static void Reset()
    {
        System.Array.Clear(generations);
        System.Array.Clear(spawnedByFight);
        System.Array.Clear(pending);
        fight.Clear();
        bestiaryCompany = null;
    }

    public static int Generation(NPC npc) => generations[npc.whoAmI];

    /// <summary>Called for every NPC spawn: a new generation for the slot, and whether it is a fight's company by parentage.</summary>
    public static void Spawned(NPC npc, IEntitySource? source)
    {
        if ((uint)npc.whoAmI >= (uint)generations.Length) return;
        generations[npc.whoAmI]++;
        pending[npc.whoAmI] = null;
        spawnedByFight[npc.whoAmI] = source is EntitySource_Parent { Entity: NPC parent } && parent.active
            && (IsBossBody(HolderOf(parent)) || IsCompany(HolderOf(parent)));
    }

    /// <summary>The NPC holding this body's life: itself, or the head a segment's <c>realLife</c> names.</summary>
    public static NPC HolderOf(NPC npc)
        => npc.realLife >= 0 && npc.realLife < Main.maxNPCs && npc.realLife != npc.whoAmI && Main.npc[npc.realLife].active
            ? Main.npc[npc.realLife] : npc;

    public static bool IsBossBody(NPC holder) => holder.boss || (holder.type >= 0 && holder.type < NPCID.Sets.ShouldBeCountedAsBoss.Length && NPCID.Sets.ShouldBeCountedAsBoss[holder.type]);

    public static bool IsCompany(NPC holder)
    {
        if (IsBossBody(holder)) return false;
        bestiaryCompany ??= new HashSet<int>(NPCID.Sets.BossBestiaryPriority ?? new List<int>());
        return bestiaryCompany.Contains(holder.type) || ((uint)holder.whoAmI < (uint)spawnedByFight.Length && spawnedByFight[holder.whoAmI]);
    }

    /// <summary>
    /// Whether a kill of this NPC may ever earn or anchor anything: never a friendly or town NPC, a critter, a statue's spawn,
    /// the target dummy or anything immortal.
    /// </summary>
    public static bool Counts(NPC holder)
        => !holder.friendly && !holder.townNPC && !holder.immortal && !holder.SpawnedFromStatue && holder.type != NPCID.TargetDummy
            && !(holder.type >= 0 && holder.type < NPCID.Sets.CountsAsCritter.Length && NPCID.Sets.CountsAsCritter[holder.type]);

    /// <summary>
    /// Whose projectile this is. A companion shot is one the arsenal registered, or a child the outcome windows joined to one;
    /// the player's is a friendly projectile he owns that no trap or town NPC fired; everything else is someone else's.
    /// </summary>
    public static Striker StrikerOf(Projectile projectile)
    {
        if (Weapons.TrackLandedHits.IsCompanionShot(projectile.whoAmI)) return Striker.Companion;
        return projectile.friendly && projectile.owner == Main.myPlayer && !projectile.trap && !projectile.npcProj ? Striker.Player : Striker.Other;
    }

    /// <summary>The before half: a strike by <paramref name="by"/> is about to land on <paramref name="target"/>.</summary>
    public static void BeforeStrike(NPC target, Striker by)
    {
        if ((uint)target.whoAmI >= (uint)pending.Length || !target.active) return;
        NPC holder = HolderOf(target);
        bool body = IsBossBody(holder);
        var snapshot = new Snapshot(holder.whoAmI, Generation(holder), holder.type, holder.lifeMax, by, Counts(holder), body,
            !body && IsCompany(holder), holder.Center);
        pending[target.whoAmI] = snapshot;
        if (body && snapshot.Counts) Join(holder).LastStriker = by;
    }

    /// <summary>The after half: the strike on <paramref name="target"/> has landed; credit it if it killed the body.</summary>
    public static void AfterStrike(NPC target)
    {
        if ((uint)target.whoAmI >= (uint)pending.Length || pending[target.whoAmI] is not { } snapshot) return;
        pending[target.whoAmI] = null;
        NPC holder = Main.npc[snapshot.Slot];
        bool died = !holder.active || Generation(holder) != snapshot.Generation;
        if (!died || !snapshot.Counts || snapshot.Body || snapshot.Company || snapshot.By == Striker.Other) return;
        if (Ledger() is not { } ledger) return;
        var credit = ledger.CreditEnemyKill(snapshot.LifeMax, snapshot.By == Striker.Companion);
        Record(ledger, credit, "enemy-kill", snapshot.By, snapshot.Where, $"type={snapshot.Type};life={snapshot.LifeMax}");
    }

    /// <summary>A body died through the game's loot path; kept beside the tick sweep, which also reads a body that left dead.</summary>
    public static void Killed(NPC npc)
    {
        if (fight.TryGetValue(Key(npc.whoAmI, Generation(npc)), out Body? body)) body.Died = true;
    }

    private static long Key(int slot, int generation) => ((long)generation << 16) | (uint)slot;

    private static Body Join(NPC holder)
    {
        long key = Key(holder.whoAmI, Generation(holder));
        if (!fight.TryGetValue(key, out Body? body))
            fight[key] = body = new Body { Slot = holder.whoAmI, Generation = Generation(holder), LifeMax = holder.lifeMax };
        else if (holder.lifeMax > body.LifeMax)
            body.LifeMax = holder.lifeMax;
        return body;
    }

    /// <summary>
    /// Once per tick, after NPCs update: every living counting boss body joins the fight, and a fight whose every body has
    /// left is ended — credited if any body died, dropped as a despawn if none did.
    /// </summary>
    public static void Sweep(ulong tick)
    {
        for (int i = 0; i < Main.maxNPCs; i++)
        {
            NPC npc = Main.npc[i];
            if (npc.active && npc.life > 0 && HolderOf(npc) == npc && IsBossBody(npc) && Counts(npc)) Join(npc);
        }
        if (fight.Count == 0) return;
        bool anyAlive = false;
        foreach (Body body in fight.Values)
        {
            NPC npc = Main.npc[body.Slot];
            bool same = Generation(npc) == body.Generation;
            if (same && npc.active) { anyAlive = true; continue; }
            if (body.LeftTick == 0)
            {
                body.LeftTick = tick == 0 ? 1 : tick;
                if (same && npc.life <= 0) body.Died = true;
            }
        }
        if (anyAlive) return;

        long wholeLife = 0;
        Body? lastDeath = null;
        foreach (Body body in fight.Values)
        {
            wholeLife += body.LifeMax;
            if (body.Died && (lastDeath == null || body.LeftTick >= lastDeath.LeftTick)) lastDeath = body;
        }
        fight.Clear();
        if (lastDeath == null || Ledger() is not { } ledger) return;
        bool byCompanion = lastDeath.LastStriker == Striker.Companion;
        var credit = ledger.CreditBossFight(wholeLife, byCompanion);
        Record(ledger, credit, "boss-fight", lastDeath.LastStriker, Main.npc[lastDeath.Slot].Center, $"whole-life={wholeLife}");
    }

    internal static CompanionExperience? Ledger()
        => Main.LocalPlayer is { } player && player.TryGetModPlayer<PlayerIntegration.CompanionPlayer>(out var save) ? save.Experience : null;

    internal static void Record(CompanionExperience ledger, in CompanionExperience.Credit credit, string source, Striker by, Vector2 where, string detail)
    {
        if (credit.Earned <= 0 && !credit.EnemyAnchorChanged && !credit.BossAnchorChanged) return;
        string anchor = credit.EnemyAnchorChanged ? "enemy" : credit.BossAnchorChanged ? "boss" : "none";
        GodsEyeEvents.RecordExperienceCredit(source, by == Striker.Companion ? "companion" : by == Striker.Player ? "player" : "other",
            credit.Earned / CompanionExperience.ExperiencePerLife, credit.LevelBefore, credit.LevelAfter, ledger.Into / CompanionExperience.ExperiencePerLife,
            ledger.Required / CompanionExperience.ExperiencePerLife, ledger.EnemyAnchorLife, ledger.EnemyAnchorLevel, ledger.BossAnchorLife,
            ledger.BossAnchorLevel, anchor, where, detail);
    }
}

/// <summary>The game's hooks, delegating to <see cref="CreditKillsAndFights"/>, which owns every decision.</summary>
public sealed class ObserveKillsForExperience : GlobalNPC
{
    public override void OnSpawn(NPC npc, IEntitySource source) => CreditKillsAndFights.Spawned(npc, source);

    public override void ModifyHitByProjectile(NPC npc, Projectile projectile, ref NPC.HitModifiers modifiers)
        => CreditKillsAndFights.BeforeStrike(npc, CreditKillsAndFights.StrikerOf(projectile));

    public override void OnHitByProjectile(NPC npc, Projectile projectile, NPC.HitInfo hit, int damageDone)
        => CreditKillsAndFights.AfterStrike(npc);

    // Only the local player's own melee reaches these: the companion's swing goes through Player.ApplyDamageToNPC, which runs
    // no NPC item hook, and brackets its own strike instead.
    public override void ModifyHitByItem(NPC npc, Player player, Item item, ref NPC.HitModifiers modifiers)
        => CreditKillsAndFights.BeforeStrike(npc, player.whoAmI == Main.myPlayer ? Striker.Player : Striker.Other);

    public override void OnHitByItem(NPC npc, Player player, Item item, NPC.HitInfo hit, int damageDone)
        => CreditKillsAndFights.AfterStrike(npc);

    public override void OnKill(NPC npc) => CreditKillsAndFights.Killed(npc);
}

/// <summary>Drives the fight sweep after NPCs update, and clears every slot memory between worlds.</summary>
public sealed class SweepBossFights : ModSystem
{
    public override void PostUpdateNPCs() => CreditKillsAndFights.Sweep(Main.GameUpdateCount);
    public override void OnWorldLoad() => CreditKillsAndFights.Reset();
    public override void OnWorldUnload() => CreditKillsAndFights.Reset();
}
