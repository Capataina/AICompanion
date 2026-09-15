#nullable enable

using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.ModLoader;
using AICompanion.Companion.Brain.Infrastructure.Diagnostics;

namespace AICompanion.Companion.Progression;

/// <summary>Who landed a strike: the companion's own weapon, the local player's weapon, projectile, minion, sentry, dash,
/// stomp or touch, or anything else — a trap, a town NPC's shot, another player's projectile.</summary>
public enum Striker { Other, Player, Companion }

/// <summary>
/// Decides which kills earn experience, who earned them, and when a boss fight is over, and hands each credit to the
/// character's <see cref="CompanionExperience"/>.
///
/// A killing blow is found the way the game finds its own (<c>NPCKillAttempt</c> in <c>Player.StrikeNPCDirect</c> and
/// <c>Projectile.Damage</c>): the target is read just before a strike and again just after, and the strike killed it when
/// the NPC holding its life is no longer active. The NPC is held by reference rather than by slot, because the game builds
/// a new object for every spawn (<c>NPC.NewNPC</c>), so the object a strike was taken against still says whether it died
/// after its slot has been handed to something else. <c>GlobalNPC.OnKill</c> is not used for this, because NPCLoot returns
/// before it whenever a mod's PreKill refuses. A death with no strike around it — a debuff ticking, lava, falling — is
/// nobody's killing blow and earns nothing.
///
/// A worm or any body sharing <c>realLife</c> is one enemy: every strike is recorded against the NPC that holds the life,
/// and <c>StrikeNPC</c> runs that NPC's <c>checkDead</c> inside the strike, so the whole worm dies inside the strike that
/// killed it and counts once at its whole life.
///
/// A boss fight is every NPC holding its own life that belongs to one boss encounter: the bodies flagged <c>boss</c> or
/// <c>NPCID.Sets.ShouldBeCountedAsBoss</c>, and, while any such body lives, their parts and minions — an NPC in
/// <c>NPCID.Sets.BossBestiaryPriority</c> or one a member's own AI spawned. The fight's life is every member's maximum life
/// except the members that do not have to die, which the fight itself shows: a type the fight spawns again after one of
/// its members of that type has left is endless — servants, probes, bees, leeches — and a member nothing could ever hurt
/// is scenery. So the Eater of Worlds is every segment whichever end dies first, the Brain of Cthulhu is the brain and its
/// creepers, and Golem is its body, head and fists; the Destroyer's segments and the Wall of Flesh's eyes hold no life of
/// their own and are one member with the NPC whose life they share. The owner's ruling of 15 September 2026.
///
/// The fight ends on the tick no boss body is left. It is dropped as a despawn when every boss body left alive, and
/// otherwise credited once, at its whole life, to whichever of the companion or the player struck any of its members last:
/// in full for the companion, in half for the player, and not at all when neither ever struck it. A fight's credit follows
/// the last strike rather than the strike that ended a body, because a boss may pass through a death state first (Moon
/// Lord's core resets its life and turns invulnerable before it dies) and may end to a debuff nobody finished.
///
/// A minion or part that dies while no boss body lives is still the boss's company and never an ordinary enemy: counted as
/// ordinary enemies, a Prime arm's life would pin the enemy anchor for the rest of the game.
/// </summary>
public static class CreditKillsAndFights
{
    private readonly record struct Snapshot(NPC Holder, ulong Tick, int Type, int LifeMax, Striker By, bool Counts, bool Member, Vector2 Where);

    private static readonly bool[] spawnedByFight = new bool[Main.maxNPCs + 1];
    // The strike in progress per target slot, taken before the strike and consumed after it.
    private static readonly Snapshot?[] pending = new Snapshot?[Main.maxNPCs + 1];

    private sealed class Member
    {
        public required NPC Npc;
        public int Type, LifeMax;
        public bool EverBoss, EverHurtable, Left, Died;
    }

    private static readonly Dictionary<NPC, Member> fight = new(ReferenceEqualityComparer.Instance);
    // Types with a member that has left this fight, and the types spawned again after that: the endless ones.
    private static readonly HashSet<int> typesLeft = new(), endless = new();
    private static Striker lastStriker = Striker.Other;
    private static Vector2 lastStrikeAt;
    private static HashSet<int>? bestiaryCompany;

    /// <summary>How many NPCs holding their own life the open fight has seen; zero with no fight.</summary>
    public static int FightMembers => fight.Count;

    public static void Reset()
    {
        System.Array.Clear(spawnedByFight);
        System.Array.Clear(pending);
        EndFight();
        bestiaryCompany = null;
    }

    private static void EndFight()
    {
        fight.Clear();
        typesLeft.Clear();
        endless.Clear();
        lastStriker = Striker.Other;
    }

    /// <summary>
    /// Called for every NPC spawn: whether the new NPC is a fight's company by parentage, and whether its type has now
    /// been spawned again after one of its fight's members of that type left, which makes the type endless.
    /// </summary>
    public static void Spawned(NPC npc, IEntitySource? source)
    {
        if ((uint)npc.whoAmI >= (uint)spawnedByFight.Length) return;
        pending[npc.whoAmI] = null;
        spawnedByFight[npc.whoAmI] = source is EntitySource_Parent { Entity: NPC parent } && parent.active
            && (IsBossBody(HolderOf(parent)) || IsCompany(HolderOf(parent)));
        if (fight.Count == 0) return;
        NoteDepartures();
        if (typesLeft.Contains(npc.type) && IsCompany(npc)) endless.Add(npc.type);
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
        bool counts = Counts(holder);
        bool member = IsBossBody(holder) || IsCompany(holder);
        pending[target.whoAmI] = new Snapshot(holder, Main.GameUpdateCount, holder.type, holder.lifeMax, by, counts, member, holder.Center);
        if (!counts || !member || (!IsBossBody(holder) && !FightOpen())) return;
        Observe(Join(holder), holder);
        if (by == Striker.Other) return;
        lastStriker = by;
        lastStrikeAt = holder.Center;
    }

    /// <summary>
    /// The before half of a strike the local player's own hooks saw: every strike he lands reaches them, a weapon's, a
    /// projectile's and a direct one — a dash, a mount's stomp, a touch, a debuff aura — alike, so the strike is taken as
    /// his only when nothing more specific already took it this tick. A projectile's strike is taken first by the NPC's
    /// projectile hook, which knows whether the shot is the companion's, and the companion's own swing brackets itself
    /// before it calls the game's strike.
    /// </summary>
    public static void BeforePlayerStrike(Player player, NPC target)
    {
        if (player.whoAmI != Main.myPlayer || (uint)target.whoAmI >= (uint)pending.Length) return;
        if (pending[target.whoAmI] is { } taken && taken.Tick == Main.GameUpdateCount) return;
        BeforeStrike(target, Striker.Player);
    }

    /// <summary>The after half: the strike on <paramref name="target"/> has landed; credit it if it killed an ordinary enemy.</summary>
    public static void AfterStrike(NPC target)
    {
        if ((uint)target.whoAmI >= (uint)pending.Length || pending[target.whoAmI] is not { } snapshot) return;
        pending[target.whoAmI] = null;
        if (snapshot.Holder.active || !snapshot.Counts || snapshot.Member || snapshot.By == Striker.Other) return;
        if (Ledger() is not { } ledger) return;
        var credit = ledger.CreditEnemyKill(snapshot.LifeMax, snapshot.By == Striker.Companion);
        Record(ledger, credit, "enemy-kill", snapshot.By, snapshot.Where, $"type={snapshot.Type};life={snapshot.LifeMax}");
    }

    private static bool FightOpen()
    {
        foreach (Member member in fight.Values)
            if (!member.Left && member.EverBoss) return true;
        return false;
    }

    private static Member Join(NPC holder)
    {
        if (!fight.TryGetValue(holder, out Member? member))
            fight[holder] = member = new Member { Npc = holder, Type = holder.type };
        return member;
    }

    private static void Observe(Member member, NPC npc)
    {
        member.LifeMax = System.Math.Max(member.LifeMax, npc.lifeMax);
        member.EverBoss |= IsBossBody(npc);
        member.EverHurtable |= !npc.dontTakeDamage;
    }

    /// <summary>Every member no longer in the world is marked left, and died when its own object ran out of life.</summary>
    private static void NoteDepartures()
    {
        foreach (Member member in fight.Values)
        {
            if (member.Left) continue;
            NPC npc = member.Npc;
            if (npc.active && ReferenceEquals(Main.npc[npc.whoAmI], npc)) continue;
            member.Left = true;
            member.Died = npc.life <= 0;
            typesLeft.Add(member.Type);
        }
    }

    /// <summary>
    /// Once per tick, after NPCs update: every living counting boss body joins or opens the fight, and while one lives so
    /// does its company; a fight whose every boss body has left is ended — credited unless every boss body left alive.
    /// </summary>
    public static void Sweep(ulong tick)
    {
        for (int i = 0; i < Main.maxNPCs; i++)
        {
            NPC npc = Main.npc[i];
            if (npc.active && npc.life > 0 && HolderOf(npc) == npc && IsBossBody(npc) && Counts(npc)) Observe(Join(npc), npc);
        }
        if (fight.Count == 0) return;
        for (int i = 0; i < Main.maxNPCs; i++)
        {
            NPC npc = Main.npc[i];
            if (npc.active && npc.life > 0 && HolderOf(npc) == npc && IsCompany(npc) && Counts(npc)) Observe(Join(npc), npc);
        }
        foreach (Member member in fight.Values)
            if (!member.Left && member.Npc.active) Observe(member, member.Npc);
        NoteDepartures();
        if (FightOpen()) return;

        long wholeLife = 0;
        bool bossDied = false;
        Vector2 where = lastStrikeAt;
        foreach (Member member in fight.Values)
        {
            if (member.EverBoss && member.Died) { bossDied = true; where = member.Npc.Center; }
            if (member.EverHurtable && !endless.Contains(member.Type)) wholeLife += member.LifeMax;
        }
        Striker by = lastStriker;
        EndFight();
        if (!bossDied || by == Striker.Other || Ledger() is not { } ledger) return;
        var credit = ledger.CreditBossFight(wholeLife, by == Striker.Companion);
        Record(ledger, credit, "boss-fight", by, where, $"whole-life={wholeLife}");
    }

    internal static CompanionExperience? Ledger()
        => Main.LocalPlayer is { } player && player.TryGetModPlayer<PlayerIntegration.CompanionPlayer>(out var save) ? save.Experience : null;

    /// <summary>Writes the credit's occurrence, every amount in the terms of the world being played, as the display shows them;
    /// the earner is always the companion or the player, because nothing else earns.</summary>
    internal static void Record(CompanionExperience ledger, in CompanionExperience.Credit credit, string source, Striker by, Vector2 where, string detail)
    {
        if (credit.Earned <= 0 && !credit.EnemyAnchorChanged && !credit.BossAnchorChanged) return;
        string anchor = credit.EnemyAnchorChanged ? "enemy" : credit.BossAnchorChanged ? "boss" : "none";
        double unit = CompanionExperience.ExperiencePerLife;
        GodsEyeEvents.RecordExperienceCredit(source, by == Striker.Companion ? "companion" : "player",
            ledger.InWorldTerms(credit.Earned) / unit, credit.LevelBefore, credit.LevelAfter, ledger.InWorldTerms(ledger.Into) / unit,
            ledger.InWorldTerms(ledger.Required) / unit, ledger.InWorldTerms(ledger.EnemyAnchorLife), ledger.EnemyAnchorLevel,
            ledger.InWorldTerms(ledger.BossAnchorLife), ledger.BossAnchorLevel, anchor, where, detail);
    }
}

/// <summary>The NPC's own hooks, delegating to <see cref="CreditKillsAndFights"/>, which owns every decision.</summary>
public sealed class ObserveKillsForExperience : GlobalNPC
{
    public override void OnSpawn(NPC npc, IEntitySource source) => CreditKillsAndFights.Spawned(npc, source);

    public override void ModifyHitByProjectile(NPC npc, Projectile projectile, ref NPC.HitModifiers modifiers)
        => CreditKillsAndFights.BeforeStrike(npc, CreditKillsAndFights.StrikerOf(projectile));

    public override void OnHitByProjectile(NPC npc, Projectile projectile, NPC.HitInfo hit, int damageDone)
        => CreditKillsAndFights.AfterStrike(npc);
}

/// <summary>
/// The local player's hooks, delegating to <see cref="CreditKillsAndFights"/>. tModLoader calls them for every strike the
/// player lands (<c>PlayerLoader.ModifyHitNPCWithItem</c> and <c>ModifyHitNPCWithProj</c> call them first, and
/// <c>Player.ApplyDamageToNPC</c> calls nothing else), so they are the only door a dash, a mount's stomp, a touch or an aura
/// comes through, and the one for his melee too.
/// </summary>
public sealed class ObservePlayerStrikesForExperience : ModPlayer
{
    public override void ModifyHitNPC(NPC target, ref NPC.HitModifiers modifiers) => CreditKillsAndFights.BeforePlayerStrike(Player, target);

    public override void OnHitNPC(NPC target, NPC.HitInfo hit, int damageDone) => CreditKillsAndFights.AfterStrike(target);
}

/// <summary>Drives the fight sweep after NPCs update, and clears every slot memory between worlds.</summary>
public sealed class SweepBossFights : ModSystem
{
    public override void PostUpdateNPCs() => CreditKillsAndFights.Sweep(Main.GameUpdateCount);
    public override void OnWorldLoad() => CreditKillsAndFights.Reset();
    public override void OnWorldUnload() => CreditKillsAndFights.Reset();
}
