#nullable enable

extern alias live;

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.Json;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using live::AICompanion.Companion.Brain.Activities;
using live::AICompanion.Companion.Brain.Activities.Combat.Planning;
using live::AICompanion.Companion.Brain.Infrastructure.Interactions.Firing;
using live::AICompanion.Companion.Brain.Infrastructure.Movement;
using live::AICompanion.Companion.Brain.Infrastructure.Observation;
using live::AICompanion.Companion.Brain.Infrastructure.Position;
using live::AICompanion.Companion.Brain.Infrastructure.WeaponKnowledge;
using live::AICompanion.Companion.CharacterBody;
using S = live::AICompanion.Companion.Brain.Activities.Combat.Planning.ExportCombatSnapshot;

namespace AICompanion.Tools.CombatAudit;

/// <summary>
/// One snapshot restored into the audit host: tiles, actors, knowledge, tracks, ledgers and the region,
/// translated from live world coordinates into the local window. Everything numeric the snapshot carried
/// is restored exactly — floats round-trip through JSON, tiles are integers — so a replay that diverges
/// from the committed plan diverges for a reason the verdict can name, not for a lossy restore. Names
/// that do not resolve headless (modded content) are collected rather than failing: the replay runs at
/// priors for those, and the verdict says so.
/// </summary>
internal sealed class RestoredDecision
{
    public S.SnapshotDto Snapshot;
    public Vector2 Shift;
    public CompanionNPC Companion = null!;
    public ActionContext Ctx;
    public IReadOnlyList<EnemyForecast> Enemies = Array.Empty<EnemyForecast>();
    public CombatWeights Weights;
    public AttackPlan? CommittedShifted;
    public List<StandProposal> Proposals = new();
    public List<StandVerdict> Verdicts = new();
    public int KnowledgeSkipped;
    public List<string> Unresolved = new();

    public RestoredDecision(S.SnapshotDto snapshot) => Snapshot = snapshot;
}

internal static class RestoreSnapshot
{
    public static RestoredDecision Restore(string json)
    {
        S.SnapshotDto? snapshot;
        try
        {
            snapshot = JsonSerializer.Deserialize<S.SnapshotDto>(json);
        }
        catch (Exception error)
        {
            throw new AuditException($"snapshot is not JSON: {error.Message}");
        }
        if (snapshot == null || snapshot.Version != S.SchemaVersion)
            throw new AuditException($"snapshot schema {snapshot?.Version} is not {S.SchemaVersion}");
        var restored = new RestoredDecision(snapshot);
        var identity = new WeaponIdentity();
        restored.Shift = AuditHost.SizeWorld(snapshot.Terrain.X, snapshot.Terrain.Y,
            snapshot.Terrain.Width, snapshot.Terrain.Height);
        RestoreTiles(restored);
        AuditHost.CreateActors();
        RestorePlayer(restored, identity);
        RestoreCompanion(restored);
        RestoreNpcs(restored, identity);
        RestoreProjectiles(restored, identity);
        typeof(Main).GetField("_gameUpdateCount", BindingFlags.Static | BindingFlags.NonPublic)!
            .SetValue(null, (uint)snapshot.GameTick);
        (_, restored.KnowledgeSkipped) = PersistWeaponKnowledge.ImportBundle(snapshot.Knowledge, identity);
        CompanionNPC companion = AuditHost.Companion;
        // Tracks before the first update: the threat sense observes motion as it builds its records,
        // and observing with a blank track bakes the wrong speed and confidence into them. With the
        // restored tracks in place the observe early-returns on the same position, velocity and tick.
        RestoreTracks(restored, identity);
        // Threat memory with them, for the same reason one layer up: a fresh slot's peak speed is its
        // first observation, and the live decision divided by the decayed peak of a history the replay
        // never lived. A trusted slot builds its records from the stamped values instead of re-decaying.
        RestoreThreatMemory(restored);
        // Generations and recent shots with the memory, ahead of the same update: the threat sense reads
        // both while it builds its records — a zero-damage caster is a threat only through its shot, and
        // a slot whose generation the restore never installed mismatches every validity target it names.
        RestoreHostileShots(restored);
        companion.Brain.Senses.Update(companion.NPC, Main.player[Main.myPlayer]);
        // The tick the snapshot was written at: the update above ran the audit's first tick, and everything
        // downstream — the sampler's draws, the deferral waits, the replayed search — reads the live tick.
        companion.Brain.Senses.AssumeTick(restored.Snapshot.SensesTick);
        companion.Brain.Senses.Intent.AssumeRegion(new PlayerIntentRegion(
            Shift(restored, snapshot.Player.Region.Centre), V(snapshot.Player.Region.Half),
            V(snapshot.Player.Region.Lead), snapshot.Player.Region.Travelling)
        {
            Heading = Shift(restored, snapshot.Player.Region.Heading),
            Velocity = V(snapshot.Player.Region.Velocity),
        });
        RestoreAuthorship(restored, identity);
        companion.Combat.AssumeCooldown(snapshot.Cooldown);
        foreach (S.DeferredDto deferred in snapshot.Deferred)
        {
            // The live equality rebuilt in the audit's revision space: equal live reopens never, a stall
            // the player's digging already reopened reopens here too. Pre-stamp snapshots carry zeroes
            // for both and land on today's old behaviour — stamped current, so they hold.
            int terrain = TerrainChanges.Revision + (deferred.Terrain - snapshot.TerrainRevision);
            companion.Combat.Planner.AssumeDeferred(deferred.Slot, deferred.Generation, deferred.Remaining,
                Shift(restored, deferred.Target), Shift(restored, deferred.Body),
                companion.Brain.Senses.Tick, terrain);
        }
        // Hits stay unstaged: they are per-commitment state and Commit clears them, so the hold
        // runner installs them after committing the shifted plan. Nothing else headless Validates.
        companion.Mana.Sync(Main.player[Main.myPlayer]);
        companion.Mana.Assume(snapshot.Body.Mana);
        restored.Companion = companion;
        // The class-scaled base per weapon: touching Weapons rebuilds on the restored gear, and the
        // stamped scalar stands in for the live bonus stack the restored player does not wear. A
        // pre-stamp capture carries none and prices naked, as every restore before it did.
        List<float>? scaled = snapshot.WeaponScaledDamage;
        if (scaled != null)
        {
            IReadOnlyList<CompanionWeapon> weapons = companion.Combat.Weapons;
            for (int v = 0; v < weapons.Count && v < scaled.Count; v++)
                weapons[v].AuditDamageOverride = scaled[v];
        }
        restored.Ctx = new ActionContext(companion, companion.Brain.Senses);
        restored.Enemies = companion.Combat.EnsureForecast(restored.Ctx);
        float[] w = snapshot.Weights;
        restored.Weights = new CombatWeights(w[0], w[1], w[2], w[3], w[4], w[5], w[6], w[7]);
        foreach (S.VerdictDto verdict in snapshot.Verdicts)
        {
            if (!Enum.IsDefined(typeof(StandReason), verdict.Reason) || !Enum.IsDefined(typeof(ReachVerdict), verdict.Reach))
                throw new AuditException($"verdict names unknown reason {verdict.Reason} or reach {verdict.Reach}");
            restored.Proposals.Add(new StandProposal(Shift(restored, verdict.Stand), (StandReason)verdict.Reason,
                verdict.WeaponSlot, verdict.Targets));
            restored.Verdicts.Add(new StandVerdict(Shift(restored, verdict.Stand), (ReachVerdict)verdict.Reach,
                verdict.Travel, verdict.HarmAt, verdict.HarmAlong, verdict.InAllowance, verdict.Why));
        }
        if (snapshot.Plan != null)
            restored.CommittedShifted = ShiftPlan(snapshot.Plan, restored.Shift,
                companion.Brain.Senses.Tick - snapshot.SensesTick);

        return restored;
    }

    public static Vector2 V(S.Vec v) => new(v.X, v.Y);

    public static Vector2 Shift(RestoredDecision restored, S.Vec v) => V(v) + restored.Shift;

    /// <summary>The snapshot's allowance admission rebuilt: the circle the live search was admitted
    /// against. The replay and the hold grade the same decision, so they share the one query.</summary>
    public static Func<Vector2, bool> AllowanceQuery(RestoredDecision restored)
    {
        float radius = restored.Snapshot.AllowanceRadius;
        Vector2 heading = Shift(restored, restored.Snapshot.Player.Region.Heading);
        Vector2 feet = restored.Ctx.Npc.Bottom;
        return point => Vector2.DistanceSquared(point, heading) <= radius * radius
            && Vector2.DistanceSquared(feet, heading) <= radius * radius;
    }

    public static void GrowFlood(RestoredDecision restored)
    {
        var senses = restored.Companion.Brain.Senses;
        var reach = senses.Reach;
        reach.Refresh(senses);
        for (int i = 0; i < 20000 && !reach.Complete; i++)
            reach.Grow();
        // The positioner reads the flood through a reference it binds incidentally while resolving,
        // so a replay that never resolved reads NotYet over finished ground. An admission query binds
        // it without moving anything: maxSolves zero prices nothing and restores what it held.
        restored.Companion.Brain.Positioner.PrepareOffer(
            new PositionRequest(RequestKind.FireFrom, restored.Companion.NPC.Center), senses, null);
    }

    private static void RestoreTiles(RestoredDecision restored)
    {
        S.TerrainDto terrain = restored.Snapshot.Terrain;
        if (terrain.Glyphs.Length != terrain.Width * terrain.Height)
            throw new AuditException($"terrain glyphs {terrain.Glyphs.Length} do not cover {terrain.Width}x{terrain.Height}");
        byte[] liquids, materials, states;
        try
        {
            liquids = Convert.FromBase64String(terrain.Liquids);
            materials = Convert.FromBase64String(terrain.Materials);
            states = Convert.FromBase64String(terrain.States);
        }
        catch (FormatException error)
        {
            throw new AuditException($"terrain arrays are not base64: {error.Message}");
        }
        if (liquids.Length != terrain.Width * terrain.Height * 2
            || materials.Length != terrain.Width * terrain.Height * 4
            || states.Length != terrain.Width * terrain.Height)
            throw new AuditException("terrain arrays do not cover the window");
        for (int x = 0; x < Main.maxTilesX; x++)
            for (int y = 0; y < Main.maxTilesY; y++)
            {
                Tile tile = Main.tile[x, y];
                tile.HasTile = true;
                tile.TileType = TileID.Dirt;
            }
        int index = 0;
        for (int row = 0; row < terrain.Height; row++)
            for (int col = 0; col < terrain.Width; col++, index++)
            {
                int tx = col + 8, ty = row + 8;
                Tile tile = Main.tile[tx, ty];
                int tileType = materials[index * 4] | (materials[index * 4 + 1] << 8);
                tile.TileType = (ushort)tileType;
                tile.WallType = (ushort)(materials[index * 4 + 2] | (materials[index * 4 + 3] << 8));
                tile.LiquidAmount = liquids[index * 2];
                tile.LiquidType = liquids[index * 2 + 1];
                byte state = states[index];
                tile.HasTile = (state & 1) != 0;
                tile.IsActuated = (state & 2) != 0;
                tile.Slope = (SlopeType)((state >> 4) & 7);
                tile.IsHalfBlock = (state & 128) != 0;
                AuditHost.MarkSolid(tileType, (state & 4) != 0, (state & 8) != 0);
            }
    }

    private static void RestorePlayer(RestoredDecision restored, WeaponIdentity identity)
    {
        S.PlayerDto player = restored.Snapshot.Player;
        Player entity = Main.player[Main.myPlayer];
        entity.width = 20;
        entity.height = 42;
        entity.position = Shift(restored, player.Centre) - new Vector2(entity.width / 2f, entity.height / 2f);
        entity.velocity = V(player.Velocity);
        entity.statLife = player.Life;
        entity.statLifeMax2 = player.LifeMax;
        entity.statManaMax2 = player.ManaMax;
        entity.dead = player.Dead;
        entity.active = true;
        entity.statDefense = Player.DefenseStat.Default + player.Defense;
        entity.endurance = player.Endurance;
        entity.DefenseEffectiveness = Terraria.ModLoader.MultipliableFloat.One * player.DefenseEffectiveness;
        var gear = AuditHost.CompanionPlayer.Gear;
        List<int>? prefixes = restored.Snapshot.GearPrefixes;
        List<S.GearStatDto?>? stats = restored.Snapshot.GearStats;
        for (int i = 0; i < gear.Slots.Length && i < restored.Snapshot.Gear.Count; i++)
        {
            string? name = restored.Snapshot.Gear[i];
            if (name == null)
                continue;
            int? id = identity.ItemOfName(name);
            if (id == null)
            {
                restored.Unresolved.Add($"gear[{i}]={name}");
                continue;
            }
            gear.Slots[i].SetDefaults(id.Value);
            // The stamped effects, not the machinery: re-running the prefix would need loader state
            // the audit never has, and the game refuses prefixes that change nothing — a zero-
            // knockback bow cannot take a knocker — so the id is restored as a label beside the
            // numbers it produced. A pre-stamp capture carries neither and restores stock, as before.
            if (stats != null && i < stats.Count && stats[i] is { } stamped)
            {
                gear.Slots[i].damage = stamped.Damage;
                gear.Slots[i].useTime = stamped.UseTime;
                gear.Slots[i].useAnimation = stamped.UseAnimation;
                gear.Slots[i].knockBack = stamped.Knockback;
                gear.Slots[i].mana = stamped.Mana;
                gear.Slots[i].shootSpeed = stamped.ShootSpeed;
                gear.Slots[i].scale = stamped.Scale;
            }
            gear.Slots[i].prefix = prefixes != null && i < prefixes.Count ? prefixes[i] : 0;
            AuditHost.RegisterSample(id.Value);
        }
        // The class-default ammo the restored weapons derive: the live game holds every sample and
        // the audit holds only what it registers, so a bow restored without its arrow would derive
        // no ammo and price a different shot. Restored slots are vanilla or air — anything else
        // failed the name above — so probing the default is as safe as the rule that derives it.
        for (int i = 0; i < gear.Slots.Length; i++)
        {
            Item slot = gear.Slots[i];
            if (slot.IsAir || slot.useAmmo == 0)
                continue;
            var probe = new Item();
            probe.SetDefaults(slot.useAmmo);
            if (probe.ammo == slot.useAmmo)
                AuditHost.RegisterSample(slot.useAmmo);
        }
    }

    private static void RestoreCompanion(RestoredDecision restored)
    {
        S.BodyDto body = restored.Snapshot.Body;
        NPC npc = AuditHost.Companion.NPC;
        npc.position = Shift(restored, body.Centre) - new Vector2(npc.width / 2f, npc.height / 2f);
        npc.velocity = V(body.Velocity);
        npc.life = body.Life;
        npc.lifeMax = body.LifeMax;
        // The live body mirrors the player's defence every tick and can carry the fight's debuffs;
        // every input the effective-damage estimate reads is stamped, so urgency prices the same hit.
        // SuperArmor is deliberately not assigned: it is a helper over defence (set writes 9999 or 0),
        // so assigning it after defence would zero what the line above restored.
        npc.defense = body.Defense;
        npc.takenDamageMultiplier = body.TakenDamageMultiplier;
        npc.ichor = body.Ichor;
        npc.betsysCurse = body.BetsysCurse;
        npc.active = true;

    }

    private static void RestoreNpcs(RestoredDecision restored, WeaponIdentity identity)
    {
        foreach (S.NpcDto dto in restored.Snapshot.Npcs)
        {
            if ((uint)dto.Slot >= (uint)Main.maxNPCs)
                throw new AuditException($"npc slot {dto.Slot} is outside Main.npc");
            NPC npc = Main.npc[dto.Slot];
            int? type = identity.NpcOfName(dto.Type);
            if (type == null)
            {
                restored.Unresolved.Add($"npc[{dto.Slot}]={dto.Type}");
                npc.type = 0;
            }
            else
            {
                npc.SetDefaults(type.Value);
            }
            npc.position = Shift(restored, dto.Position);
            npc.velocity = V(dto.Velocity);
            npc.life = dto.Life;
            npc.lifeMax = dto.LifeMax;
            npc.defense = dto.Defense;
            npc.damage = dto.Damage;
            npc.knockBackResist = dto.KnockbackResist;
            npc.width = dto.Width;
            npc.height = dto.Height;
            Array.Clear(npc.buffType, 0, npc.buffType.Length);
            Array.Clear(npc.buffTime, 0, npc.buffTime.Length);
            for (int i = 0; i < dto.Buffs.Count && i < npc.buffType.Length; i++)
            {
                int? buff = identity.BuffOfName(dto.Buffs[i].Name);
                if (buff == null)
                {
                    restored.Unresolved.Add($"npc[{dto.Slot}].buff={dto.Buffs[i].Name}");
                    continue;
                }
                npc.buffType[i] = buff.Value;
                npc.buffTime[i] = dto.Buffs[i].Time;
            }
            npc.onFire2 = dto.OnFire;
            npc.noGravity = dto.NoGravity;
            npc.noTileCollide = dto.NoTileCollide;
            npc.active = true;
            npc.whoAmI = dto.Slot;
        }
    }

    private static void RestoreProjectiles(RestoredDecision restored, WeaponIdentity identity)
    {
        foreach (S.ProjectileDto dto in restored.Snapshot.Projectiles)
        {
            if ((uint)dto.Slot >= (uint)Main.maxProjectiles)
                throw new AuditException($"projectile slot {dto.Slot} is outside Main.projectile");
            Projectile projectile = Main.projectile[dto.Slot];
            int? type = identity.ProjectileOfName(dto.Type);
            if (type == null)
            {
                restored.Unresolved.Add($"projectile[{dto.Slot}]={dto.Type}");
                projectile.type = 0;
            }
            else
            {
                projectile.SetDefaults(type.Value);
                AuditHost.RegisterProjectileSample(type.Value);
            }
            projectile.position = Shift(restored, dto.Position);
            projectile.velocity = V(dto.Velocity);
            projectile.width = dto.Width;
            projectile.height = dto.Height;
            projectile.damage = dto.Damage;
            projectile.extraUpdates = dto.ExtraUpdates;
            projectile.hostile = true;
            projectile.friendly = false;
            projectile.active = true;
            projectile.whoAmI = dto.Slot;
        }
    }

    private static void RestoreTracks(RestoredDecision restored, WeaponIdentity identity)
    {
        foreach (S.NpcDto dto in restored.Snapshot.Npcs)
        {
            if (dto.Track == null)
                continue;
            NPC npc = Main.npc[dto.Slot];
            S.TrackDto track = dto.Track;
            PredictObservedMotion.AssumeTrack(npc, new PredictObservedMotion.ExportedTrack(dto.Slot, npc.type,
                track.Tick, Shift(restored, track.Position), V(track.Velocity), V(track.Acceleration),
                track.Gravity, track.MaxFall, track.Water, track.Lava, track.Honey, track.Shimmer,
                track.NoGravity, track.NoTileCollide, track.Wet, track.LavaWet, track.HoneyWet, track.ShimmerWet,
                track.MeanError, track.ErrorSamples));
        }
    }

    private static void RestoreThreatMemory(RestoredDecision restored)
    {
        var threats = AuditHost.Companion.Brain.Senses.Threats;
        foreach (S.NpcDto dto in restored.Snapshot.Npcs)
        {
            if (dto.Threat == null)
            {
                restored.Unresolved.Add($"npc[{dto.Slot}].threat-memory");
                continue;
            }
            threats.AssumeThreatMemory(dto.Slot, dto.Threat.PeakSpeed, dto.Threat.CanReachPlayer,
                dto.Threat.CanReachCompanion);
        }
    }

    private static void RestoreHostileShots(RestoredDecision restored)
    {
        foreach (S.NpcDto dto in restored.Snapshot.Npcs)
        {
            NPC npc = Main.npc[dto.Slot];
            if (dto.Generation == 0 && dto.ShotDamage == 0)
            {
                // A live spawn always stamps a generation of at least one, so a zero pair is a snapshot
                // written before the stamp: the replay runs generationless and shotless, and says so.
                restored.Unresolved.Add($"npc[{dto.Slot}].threat-shots");
                continue;
            }
            HostileAttackSources.AssumeGeneration(npc, dto.Generation);
            if (dto.ShotDamage > 0)
                HostileAttackSources.AssumeShot(npc, dto.Generation, (uint)dto.ShotTick, dto.ShotDamage);
        }
    }

    private static void RestoreAuthorship(RestoredDecision restored, WeaponIdentity identity)
    {
        foreach (S.NpcDto dto in restored.Snapshot.Npcs)
            foreach (S.AddedByDto author in dto.AddedBy)
            {
                int? buff = identity.BuffOfName(author.Buff);
                int? item = identity.ItemOfName(author.Item);
                if (buff == null || item == null)
                {
                    restored.Unresolved.Add($"npc[{dto.Slot}].addedBy={author.Buff}<{author.Item}");
                    continue;
                }
                ShotOutcomes.NoteAdded(Main.npc[dto.Slot], buff.Value, item.Value);
            }
    }

    private static AttackPlan ShiftPlan(S.PlanDto plan, Vector2 shift, int tickDelta)
    {
        var segments = new AttackSegment[plan.Segments.Count];
        for (int s = 0; s < segments.Length; s++)
        {
            S.SegmentDto segment = plan.Segments[s];
            var uses = new PlannedUse[segment.Uses.Count];
            for (int u = 0; u < uses.Length; u++)
            {
                S.UseDto use = segment.Uses[u];
                uses[u] = new PlannedUse(use.Weapon, V(use.Muzzle) + shift, V(use.Aim) + shift, V(use.Launch),
                    use.FireTick + tickDelta, use.Target);
            }
            S.VerdictDto verdict = segment.Verdict;
            var proposal = new StandProposal(V(segment.Stand) + shift, (StandReason)segment.Reason,
                segment.WeaponSlot, segment.Targets);
            var assessed = new StandVerdict(V(segment.Stand) + shift, (ReachVerdict)verdict.Reach, verdict.Travel,
                verdict.HarmAt, verdict.HarmAlong, verdict.InAllowance, verdict.Why);
            segments[s] = new AttackSegment(proposal, assessed, segment.Arrive + tickDelta,
                segment.Start + tickDelta, segment.End + tickDelta, uses, (SegmentEnd)segment.EndsWhen);
        }
        S.ValidityDto validity = plan.Validity;
        var targets = new (int Slot, int Generation)[validity.Targets.Count];
        for (int i = 0; i < targets.Length; i++)
            targets[i] = (validity.Targets[i][0], validity.Targets[i][1]);
        (int Slot, int Tick)[]? kills = null;
        if (plan.Kills.Count > 0)
        {
            kills = new (int Slot, int Tick)[plan.Kills.Count];
            for (int i = 0; i < kills.Length; i++)
                kills[i] = (plan.Kills[i][0], plan.Kills[i][1] + tickDelta);
        }
        float[] outcome = plan.Outcome;
        return new AttackPlan(plan.Id, segments,
            new CombatOutcome(outcome[0], outcome[1], outcome[2], outcome[3], outcome[4], outcome[5], outcome[6], outcome[7]),
            plan.Weighted,
            new PlanValidity(validity.Terrain, validity.Knowledge, targets, validity.MaxUrgency,
                V(validity.RegionCentre) + shift, V(validity.RegionHalf), validity.Gap, validity.ProgressTick + tickDelta),
            plan.BudgetCut, kills);
    }
}

internal sealed class AuditException : Exception
{
    public AuditException(string message) : base(message) { }
}
